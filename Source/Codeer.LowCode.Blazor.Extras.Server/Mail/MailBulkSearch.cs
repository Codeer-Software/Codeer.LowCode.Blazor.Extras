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
    /// Server-side recipient resolution for bulk sends: runs the search condition through
    /// ModuleDataIO (so the user's read permissions and row conditions apply), builds the
    /// per-recipient variables as display strings, then dispatches. Addresses never travel
    /// to the client on this path, which also makes it the choice for large sends.
    /// When the request names a summary field (BulkMailField), the send result is written back
    /// to that field's DB column through the internal update path after the send.
    /// </summary>
    public static class MailBulkSearch
    {
        public static async Task<MailSendResult> SendAsync(MailDispatcher dispatcher, ModuleDataIO moduleDataIO,
            DesignData designData, MailConfig config, MailBulkSearchRequest request,
            Func<ModuleData, Task>? updateRecordInternalAsync = null, Action<string>? logError = null)
        {
            var design = designData.Modules.Find(request.Condition.ModuleName)
                ?? throw new InvalidOperationException($"Module '{request.Condition.ModuleName}' does not exist.");
            if (string.IsNullOrEmpty(request.EmailAddressVariable))
                throw new InvalidOperationException("EmailAddressVariable is required for search-based bulk send.");

            //テンプレ変数+宛先/除外+Id(RecordUrl用)だけ取得する。
            //リンクパス("Contact.Email")はルートのLink/SelectFieldがあればデータ層がJOIN/INで解決する
            var names = MailRecipientBuilder.GetVariableNames(request.Subject, request.Body);
            var searchCondition = request.Condition.JsonClone();
            searchCondition.LimitCount = null; //全件(上限はMaxBulkCountが守る)
            searchCondition.SelectFields = names
                .Select(e => MailVariableResolver.ParseToken(e).FieldPath)
                .Where(e => design.Fields.Any(f => f.Name == new FieldName(e).Root))
                .Concat(new[]
                {
                    MailVariableResolver.ParseToken(request.EmailAddressVariable).FieldPath,
                    MailVariableResolver.ParseToken(request.OptOutVariable).FieldPath,
                    SystemFieldNames.Id,
                })
                .Where(e => !string.IsNullOrEmpty(e))
                .Distinct().ToList();

            var rows = (await moduleDataIO.GetListAsync(searchCondition, 0)).Items;

            var recipients = rows
                .Select(row => MailRecipientBuilder.TryBuild(design, row, request.EmailAddressVariable, request.OptOutVariable, names,
                    config.AppBaseUrl, designData.PageFrames.ResolvedMainPageFrameName, designData.Modules.Find))
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
            var result = await dispatcher.SendBulkAsync(request.SenderName, template, recipients,
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
                var senderName = dispatcher.ResolveSenderSettings(request.SenderName).Name;
                summaryData.Value = BulkMailSummary.Prepend(currentJson,
                    BulkMailSummary.CreateEntry(senderName, request.Subject, result, DateTime.Now));

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
