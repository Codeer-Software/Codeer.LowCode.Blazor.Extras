using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>
    /// セットアップ (承認 / 編集履歴 / 監査ログ) がユーザーモジュールを参照するときの既定値。
    /// 表示名のフィールドは、テンプレートごとに名前が違う (PatternShowcase の AppUser は "Name"、Empty の AppUser は日本語名で
    /// ログインアカウント契約の DisplayName の役割だけが指す) ので、名前の決め打ちではなく契約から引く。
    /// </summary>
    internal static class UserModuleFields
    {
        /// <summary>ユーザーモジュール名の既定: アプリ設定のカレントユーザーモジュール (未設定なら AppUser)。</summary>
        internal static string DefaultUserModuleName(DesignData designData)
            => string.IsNullOrEmpty(designData.AppSettings.CurrentUserModuleDesignName) ? "AppUser" : designData.AppSettings.CurrentUserModuleDesignName;

        /// <summary>
        /// 表示名フィールドの既定: モジュールに "Name" があればそれ、無ければログインアカウント契約の DisplayName の役割、
        /// それも無ければ "Name" (モジュールが見つからないときも "Name")。
        /// </summary>
        internal static string DefaultDisplayNameField(DesignData designData, string? userModuleName = null)
            => DefaultDisplayNameField(designData.Modules.Find(userModuleName ?? DefaultUserModuleName(designData)));

        internal static string DefaultDisplayNameField(ModuleDesign? userModule)
        {
            if (userModule == null || userModule.Fields.Any(f => f.Name == "Name")) return "Name";
            var contract = userModule.Fields.OfType<LoginAccountContractFieldDesign>().FirstOrDefault();
            return string.IsNullOrEmpty(contract?.DisplayName) ? "Name" : contract.DisplayName;
        }
    }
}
