using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Json;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// Server-side recipient resolution for bulk sends: runs the search condition through
    /// ModuleDataIO (so the user's read permissions and row conditions apply), builds the
    /// per-recipient variables as display strings, then dispatches. Addresses never travel
    /// to the client on this path, which also makes it the choice for large sends.
    /// </summary>
    public static class MailBulkSearch
    {
        public static async Task<MailSendResult> SendAsync(MailDispatcher dispatcher, ModuleDataIO moduleDataIO,
            DesignData designData, MailConfig config, MailBulkSearchRequest request)
        {
            var design = designData.Modules.Find(request.Condition.ModuleName)
                ?? throw new InvalidOperationException($"Module '{request.Condition.ModuleName}' does not exist.");
            if (string.IsNullOrEmpty(request.ToField))
                throw new InvalidOperationException("ToField is required for search-based bulk send.");

            //テンプレ変数+宛先/除外+Id(RecordUrl用)だけ取得する
            var names = MailRecipientBuilder.GetVariableNames(request.Subject, request.Body);
            var searchCondition = request.Condition.JsonClone();
            searchCondition.LimitCount = null; //全件(上限はMaxBulkCountが守る)
            searchCondition.SelectFields = names.Where(e => design.Fields.Any(f => f.Name == e))
                .Concat(new[] { request.ToField, request.ExcludeField, SystemFieldNames.Id })
                .Where(e => !string.IsNullOrEmpty(e))
                .Distinct().ToList();

            var rows = (await moduleDataIO.GetListAsync(searchCondition, 0)).Items;

            var recipients = rows
                .Select(row => MailRecipientBuilder.TryBuild(design, row, request.ToField, request.ExcludeField, names,
                    config.AppBaseUrl, designData.PageFrames.ResolvedMainPageFrameName))
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
            return await dispatcher.SendBulkAsync(request.SenderName, template, recipients,
                MailDispatcher.CreateSource(request.SourceModule, request.SourceId));
        }
    }
}
