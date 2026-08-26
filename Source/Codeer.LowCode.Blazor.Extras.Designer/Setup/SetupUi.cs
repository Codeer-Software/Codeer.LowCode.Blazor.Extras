using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Design;
using System.Windows.Controls;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>セットアップダイアログ共通の小物 (モジュールのフィールド候補など)。</summary>
    internal static class SetupUi
    {
        /// <summary>デザインの「現在のユーザーのモジュール」名 (未設定は AppUser)。</summary>
        internal static string CurrentUserModuleName(DesignData designData)
            => string.IsNullOrEmpty(designData.AppSettings.CurrentUserModuleDesignName)
                ? "AppUser" : designData.AppSettings.CurrentUserModuleDesignName;

        /// <summary>モジュールの値フィールド名 (DB 列を持つもの。Id は除く)。ドロップダウンの候補用。</summary>
        internal static List<string> ValueFieldNames(ModuleDesign? module)
            => module?.Fields.Where(e => e is DbValueFieldDesignBase && e is not IdFieldDesign).Select(e => e.Name).ToList() ?? new();

        /// <summary>コンボの候補を差し替え、preferred (無ければ fallback、それも無ければ先頭) を選択する。</summary>
        internal static void FillFields(ComboBox combo, ModuleDesign? module, string? preferred, string fallback)
        {
            combo.Items.Clear();
            foreach (var name in ValueFieldNames(module)) combo.Items.Add(name);
            combo.SelectedItem = preferred != null && combo.Items.Contains(preferred) ? preferred
                : combo.Items.Contains(fallback) ? fallback
                : combo.Items.Count > 0 ? combo.Items[0] : null;
        }
    }
}
