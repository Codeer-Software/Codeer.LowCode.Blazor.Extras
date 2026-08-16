using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// メール契約フィールドの解決。送信履歴モジュールは自分に置かれた契約フィールドで
    /// 「役割→フィールド名」を宣言する (承認の契約と同じ流儀)。
    /// </summary>
    public static class MailContracts
    {
        public static MailHistoryContractFieldDesign? History(ModuleDesign? historyModule)
            => historyModule?.Fields.OfType<MailHistoryContractFieldDesign>().FirstOrDefault();
    }
}
