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
    /// </summary>
    public class MailBulkSearch
    {
        readonly MailDispatcher _dispatcher;
        readonly ModuleDataIO _moduleDataIO;
        readonly DesignData _designData;
        readonly Action<string>? _logError;

        public MailBulkSearch(MailDispatcher dispatcher, ModuleDataIO moduleDataIO, DesignData designData,
            Action<string>? logError = null)
        {
            _dispatcher = dispatcher;
            _moduleDataIO = moduleDataIO;
            _designData = designData;
            _logError = logError;
        }

        public async Task<MailSendResult> SendAsync(MailBulkSearchRequest request)
        {
            var design = _designData.Modules.Find(request.Condition.ModuleName)
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
            var names = MailTemplateEngine.GetVariableNames(request.Subject, request.Body);
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

            var rows = (await _moduleDataIO.GetListAsync(searchCondition, 0)).Items;
            await MailLinkPathLoader.LoadAsync(_moduleDataIO, _designData, design, rows, paths);

            var recipients = rows
                .Select(row => MailRecipientBuilder.TryBuild(design, row, contract.Email, contract.OptOut, names,
                    _designData.Modules.Find))
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
                var user = await _dispatcher.GetCurrentUserAsync();
                if (user == null)
                {
                    var fromFailure = new MailSendResult
                    {
                        TotalCount = recipients.Count,
                        Failures = recipients.Select(e => new MailSendFailure { To = e.To, Error = MailDispatcher.CurrentUserUnresolvedError }).ToList(),
                    };
                    return fromFailure;
                }
                template.From = user.Email;
                template.FromDisplayName = user.DisplayName;
            }
            return await _dispatcher.SendBulkAsync(request.MailInfraName, template, recipients,
                MailDispatcher.CreateSource(request.SourceModule, request.SourceId));
        }
    }
}
