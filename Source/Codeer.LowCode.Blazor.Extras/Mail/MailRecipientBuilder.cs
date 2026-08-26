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
        /// <summary>行から宛先1件を組み立てる。除外 (配信停止) やアドレス無しの行は null を返す。</summary>
        public static MailBulkRecipient? TryBuild(ModuleDesign? design, ModuleData row, string emailAddressVariable, string optOutVariable,
            IReadOnlyCollection<string> names, Func<string, ModuleDesign?>? findModule = null)
            => Build(design, row, emailAddressVariable, optOutVariable, names, findModule).Recipient;

        /// <summary>
        /// 行から宛先1件を組み立て、除外した場合はその理由も返す (プレビューで「誰が・なぜ外れたか」を見せる)。
        /// 除外行でも変数は解決する (参考表示用)。
        /// </summary>
        public static (MailBulkRecipient? Recipient, MailRecipientExclusion Exclusion, Dictionary<string, string> Variables) Build(
            ModuleDesign? design, ModuleData row, string emailAddressVariable, string optOutVariable,
            IReadOnlyCollection<string> names, Func<string, ModuleDesign?>? findModule = null)
        {
            var variables = MailVariableResolver.Resolve(design, row, names, findModule);
            if (MailVariableResolver.GetBooleanValue(row, optOutVariable))
                return (null, MailRecipientExclusion.OptOut, variables);
            var to = MailVariableResolver.GetValueText(row, emailAddressVariable);
            if (string.IsNullOrEmpty(to))
                return (null, MailRecipientExclusion.NoAddress, variables);
            return (new MailBulkRecipient { To = to, Variables = variables }, MailRecipientExclusion.None, variables);
        }
    }

    /// <summary>一斉送信で行が除外された理由。</summary>
    internal enum MailRecipientExclusion
    {
        None,
        /// <summary>配信停止 (宛先契約の OptOut が true)。</summary>
        OptOut,
        /// <summary>宛先アドレスが空。</summary>
        NoAddress,
    }
}
