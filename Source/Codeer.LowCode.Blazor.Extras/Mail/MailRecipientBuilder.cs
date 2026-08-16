using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// モジュール行から一斉送信の宛先を組み立てる: 配信停止フラグの行 (optOutVariable) と
    /// アドレスの無い行 (emailAddressVariable) はスキップし、テンプレート変数を表示文字列として
    /// 解決する。クライアント (行リスト) とサーバー (検索ベース送信) で共有。
    /// </summary>
    internal static class MailRecipientBuilder
    {
        /// <summary>サーバー解決経路でレコードへのディープリンクに解決される (Mail.AppBaseUrl が必要)。</summary>
        public const string RecordUrlVariable = "RecordUrl";

        /// <summary>件名・本文テンプレートで使われている変数名の一覧 (重複なし)。</summary>
        public static List<string> GetVariableNames(string subject, string body)
            => MailTemplateEngine.GetVariableNames(subject)
                .Concat(MailTemplateEngine.GetVariableNames(body))
                .Distinct().ToList();

        /// <summary>
        /// 行から宛先1件を組み立てる。除外 (配信停止) やアドレス無しの行は null を返す。
        /// {RecordUrl} は recordUrlBase 設定時 (サーバー解決経路) のみ提供される。
        /// </summary>
        public static MailBulkRecipient? TryBuild(ModuleDesign? design, ModuleData row, string emailAddressVariable, string optOutVariable,
            IReadOnlyCollection<string> names, string recordUrlBase = "", string mainPageFrame = "",
            Func<string, ModuleDesign?>? findModule = null)
        {
            if (MailVariableResolver.GetBooleanValue(row, optOutVariable)) return null; //オプトアウト
            var to = MailVariableResolver.GetValueText(row, emailAddressVariable);
            if (string.IsNullOrEmpty(to)) return null;

            var variables = MailVariableResolver.Resolve(design, row, names.Where(e => e != RecordUrlVariable), findModule);
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
