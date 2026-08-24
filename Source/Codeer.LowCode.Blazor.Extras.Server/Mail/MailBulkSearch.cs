using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// 一斉送信の宛先をサーバー側で解決する: 検索条件を ModuleDataIO に通し
    /// (ユーザーの読み取り権限・行条件が効く)、宛先ごとの変数を表示文字列として組み立てて
    /// ディスパッチする。この経路ではアドレスがクライアントに渡らないため、大量送信もこちらを使う。
    /// リクエストにサマリフィールド (BulkMailField) が指定されていれば、送信後に
    /// そのフィールドの DB 列へ内部更新経路で結果を書き戻す。
    /// </summary>
    public static class MailBulkSearch
    {
        public static async Task<MailSendResult> SendAsync(MailDispatcher dispatcher, ModuleDataIO moduleDataIO,
            DesignData designData, MailBulkSearchRequest request,
            Func<ModuleData, Task>? updateRecordInternalAsync = null, Action<string>? logError = null)
        {
            var design = designData.Modules.Find(request.Condition.ModuleName)
                ?? throw new InvalidOperationException($"Module '{request.Condition.ModuleName}' does not exist.");

            //どの値がアドレス・配信停止かは宛先(行)モジュールの契約が宣言する (クライアントからは指定できない)
            var contract = MailContracts.Recipient(design)
                ?? throw new InvalidOperationException(
                    $"Module '{design.Name}' does not implement the mail recipient contract. " +
                    "Put a BulkMailRecipientContractField on it and declare the mail address.");
            if (string.IsNullOrEmpty(contract.Email))
                throw new InvalidOperationException(
                    $"The mail address role of the recipient contract on '{design.Name}' is empty.");

            //テンプレ変数+宛先/除外+Idだけ取得する。
            //リンクパス("Contact.Email")はルートの FK を取得し、リンク先は後段で一括解決する
            var names = MailRecipientBuilder.GetVariableNames(request.Subject, request.Body);
            var paths = names
                .Select(e => MailVariableResolver.ParseToken(e).FieldPath)
                .Where(e => design.Fields.Any(f => f.Name == new FieldName(e).Root))
                .Concat(new[]
                {
                    MailVariableResolver.ParseToken(contract.Email).FieldPath,
                    MailVariableResolver.ParseToken(contract.OptOut).FieldPath,
                })
                .Where(e => !string.IsNullOrEmpty(e))
                .Distinct().ToList();
            var searchCondition = request.Condition.JsonClone();
            searchCondition.LimitCount = null; //全件(上限はMaxBulkCountが守る)
            searchCondition.SelectFields = paths
                .Select(e => new FieldName(e).Root)
                .Append(SystemFieldNames.Id)
                .Distinct().ToList();

            var rows = (await moduleDataIO.GetListAsync(searchCondition, 0)).Items;
            await MailLinkPathLoader.LoadAsync(moduleDataIO, designData, design, rows, paths);

            var recipients = rows
                .Select(row => MailRecipientBuilder.TryBuild(design, row, contract.Email, contract.OptOut, names,
                    designData.Modules.Find))
                .Where(e => e != null)
                .Select(e => e!)
                .ToList();

            var template = new MailBulkTemplate
            {
                Subject = request.Subject,
                Body = request.Body,
                IsBodyHtml = request.IsBodyHtml,
                ReplyTo = request.ReplyTo,
                Attachments = request.Attachments,
            };
            //差出人はクライアントの値を信用せず、「自分を差出人にする」のときだけサーバーが操作ユーザーを解決する
            if (request.IsFromCurrentUser)
            {
                var user = await dispatcher.GetCurrentUserAsync();
                if (user == null)
                {
                    var fromFailure = new MailSendResult
                    {
                        TotalCount = recipients.Count,
                        Failures = recipients.Select(e => new MailSendFailure { To = e.To, Error = MailDispatcher.CurrentUserUnresolvedError }).ToList(),
                    };
                    await WriteSummaryAsync(dispatcher, moduleDataIO, designData, request, fromFailure,
                        updateRecordInternalAsync, logError);
                    return fromFailure;
                }
                template.From = user.Email;
                template.FromDisplayName = user.DisplayName;
            }
            var result = await dispatcher.SendBulkAsync(request.MailInfraName, template, recipients,
                MailDispatcher.CreateSource(request.SourceModule, request.SourceId));

            await WriteSummaryAsync(dispatcher, moduleDataIO, designData, request, result,
                updateRecordInternalAsync, logError);
            return result;
        }

        //起点レコード(BulkMailField)のDB列へ送信結果サマリを書き戻す。
        //履歴と同じくシステムの記録なので、操作ユーザーの書き込み権限に依存しない内部経路で書く。
        //サマリの失敗は送信を失敗させない(ログのみ)
        static async Task WriteSummaryAsync(MailDispatcher dispatcher, ModuleDataIO moduleDataIO, DesignData designData,
            MailBulkSearchRequest request, MailSendResult result,
            Func<ModuleData, Task>? updateRecordInternalAsync, Action<string>? logError)
        {
            if (string.IsNullOrEmpty(request.SummaryFieldName) || updateRecordInternalAsync == null) return;
            if (string.IsNullOrEmpty(request.SourceModule) || string.IsNullOrEmpty(request.SourceId)) return;

            try
            {
                var sourceDesign = designData.Modules.Find(request.SourceModule);
                var summaryFieldDesign = sourceDesign?.Fields.FirstOrDefault(e => e.Name == request.SummaryFieldName);
                if (summaryFieldDesign?.CreateData() is not ValueFieldDataBase<string> summaryData)
                {
                    logError?.Invoke($"Bulk mail summary field '{request.SourceModule}.{request.SummaryFieldName}' was not found or cannot hold text.");
                    return;
                }

                //現在値を読み(権限は操作ユーザーのまま=自分のレコードを開いて送信している)、先頭に追記する
                var condition = new SearchCondition
                {
                    ModuleName = request.SourceModule,
                    Condition = new FieldValueMatchCondition
                    {
                        SearchTargetVariable = $"{SystemFieldNames.Id}.Value",
                        Comparison = MatchComparison.Equal,
                        Value = MultiTypeValue.Create(request.SourceId),
                    },
                    SelectFields = new List<string> { SystemFieldNames.Id, request.SummaryFieldName },
                };
                var record = (await moduleDataIO.GetListAsync(condition, 0)).Items.FirstOrDefault();
                if (record == null)
                {
                    logError?.Invoke($"Bulk mail summary target record '{request.SourceModule}/{request.SourceId}' was not found.");
                    return;
                }

                var currentJson = (record.Fields.GetValueOrDefault(request.SummaryFieldName) as ValueFieldDataBase<string>)?.Value;
                var mailInfraName = dispatcher.ResolveBulkInfraName(request.MailInfraName);
                summaryData.Value = BulkMailSummary.Prepend(currentJson,
                    BulkMailSummary.CreateEntry(mailInfraName, request.Subject, result, DateTime.Now));

                var update = new ModuleData { Name = request.SourceModule };
                update.Fields[SystemFieldNames.Id] = record.Fields[SystemFieldNames.Id];
                update.Fields[request.SummaryFieldName] = summaryData;
                await updateRecordInternalAsync(update);
            }
            catch (Exception e)
            {
                logError?.Invoke($"Failed to write the bulk mail summary. {e.Message}");
            }
        }
    }
}
