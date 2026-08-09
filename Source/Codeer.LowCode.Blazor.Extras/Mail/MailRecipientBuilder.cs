using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// Builds bulk mail recipients from module rows: opt-out flag rows (excludeField) and rows
    /// without an address (toField) are skipped, and the template variables are resolved as
    /// display strings. Shared by the client (row lists) and the server (search-based sends).
    /// </summary>
    internal static class MailRecipientBuilder
    {
        /// <summary>Resolved to a record deep link on the server-side search path (needs Mail.AppBaseUrl).</summary>
        public const string RecordUrlVariable = "RecordUrl";

        /// <summary>Distinct variable names used in the subject and body templates.</summary>
        public static List<string> GetVariableNames(string subject, string body)
            => MailTemplateEngine.GetVariableNames(subject)
                .Concat(MailTemplateEngine.GetVariableNames(body))
                .Distinct().ToList();

        /// <summary>
        /// Builds one recipient from a row. Returns null when the row is excluded (opt-out) or has
        /// no address. {RecordUrl} is provided only when recordUrlBase is set (server-side path).
        /// </summary>
        public static MailBulkRecipient? TryBuild(ModuleDesign? design, ModuleData row, string toField, string excludeField,
            IReadOnlyCollection<string> names, string recordUrlBase = "", string mainPageFrame = "")
        {
            if (MailVariableResolver.GetBooleanValue(row, excludeField)) return null; //オプトアウト
            var to = MailVariableResolver.GetValueText(row, toField);
            if (string.IsNullOrEmpty(to)) return null;

            var variables = MailVariableResolver.Resolve(design, row, names.Where(e => e != RecordUrlVariable));
            if (names.Contains(RecordUrlVariable) && !string.IsNullOrEmpty(recordUrlBase))
            {
                var id = (row.Fields.GetValueOrDefault(Codeer.LowCode.Blazor.DesignLogic.SystemFieldNames.Id) as IdFieldData)?.Value ?? string.Empty;
                variables[RecordUrlVariable] = string.IsNullOrEmpty(id) || design == null
                    ? string.Empty
                    : $"{recordUrlBase.TrimEnd('/')}/{mainPageFrame}/{design.Name}/{Uri.EscapeDataString(id)}";
            }
            return new MailBulkRecipient { To = to, Variables = variables };
        }
    }
}
