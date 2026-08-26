using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// メール契約フィールドの解決。送信履歴モジュールは自分に置かれた契約フィールドで
    /// 「役割→フィールド名」を宣言する (承認の契約と同じ流儀)。
    /// </summary>
    internal static class MailContracts
    {
        public static MailHistoryContractFieldDesign? History(ModuleDesign? historyModule)
            => historyModule?.Fields.OfType<MailHistoryContractFieldDesign>().FirstOrDefault();

        /// <summary>差出人 (操作ユーザー) モジュールの契約。無ければ null。</summary>
        public static MailSenderContractFieldDesign? Sender(ModuleDesign? currentUserModule)
            => currentUserModule?.Fields.OfType<MailSenderContractFieldDesign>().FirstOrDefault();

        /// <summary>一斉送信の宛先モジュールの契約。無ければ null (= そのモジュールは契約を実装していない)。</summary>
        public static BulkMailRecipientContractFieldDesign? Recipient(ModuleDesign? recipientModule)
            => recipientModule?.Fields.OfType<BulkMailRecipientContractFieldDesign>().FirstOrDefault();
    }
}
