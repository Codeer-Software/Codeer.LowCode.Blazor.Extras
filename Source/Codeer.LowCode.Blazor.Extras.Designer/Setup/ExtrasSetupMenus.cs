using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.Designer.Views.Windows;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>
    /// Extras のセットアップメニュー (Tools 配下)。
    /// アプリの OnStartup で base.OnStartup(e) の後に ExtrasDesignerInitializer.Setup(DesignerEnvironment)
    /// を呼ぶと登録される (DesignerStandard.Setup と同じタイミング)。
    /// </summary>
    public static class ExtrasSetupMenus
    {
        public static void AddAll(DesignerEnvironment env)
        {
            AddApprovalFlowSetup(env);
            AddMailSetup(env);
        }

        /// <summary>Tools &gt; 承認フローのセットアップ。承認モジュール群 (フロー系 + 経路マスタ) の生成。</summary>
        public static void AddApprovalFlowSetup(DesignerEnvironment env)
            => env.AddMainMenu(() => RunApprovalSetup(env), "Tools", Properties.Resources.SetupMenuApprovalFlow);

        /// <summary>Tools &gt; メールのセットアップ。送信履歴モジュール・サーバー設定の案内。</summary>
        public static void AddMailSetup(DesignerEnvironment env)
            => env.AddMainMenu(() => RunMailSetup(env), "Tools", Properties.Resources.SetupMenuMail);

        static void RunApprovalSetup(DesignerEnvironment env)
        {
            if (string.IsNullOrEmpty(env.CurrentFileDirectory)) return;
            try
            {
                var designData = env.GetDesignData();
                var dataSources = env.GetDesignerSettings().DataSources;

                var options = ApprovalSetupWindow.ShowDialog(designData, dataSources.Select(e => e.Name).ToList());
                if (options == null) return;

                var dataSource = dataSources.First(e => e.Name == options.DataSourceName);
                var result = ApprovalFlowSetupService.Run(designData, env.CurrentFileDirectory, options,
                    dataSource.DataSourceType, env.GetDbInfo(dataSource.Name));

                SetupResultWindow.ShowResult(env, dataSource, result);
            }
            catch (Exception ex)
            {
                MessageWindow.Show(ex.Message, "Error");
            }
        }

        static void RunMailSetup(DesignerEnvironment env)
        {
            if (string.IsNullOrEmpty(env.CurrentFileDirectory)) return;
            try
            {
                var designData = env.GetDesignData();
                var dataSources = env.GetDesignerSettings().DataSources;

                var options = MailSetupWindow.ShowDialog(designData, dataSources.Select(e => e.Name).ToList());
                if (options == null) return;

                //履歴を作らないときデータソースは未選択のことがある (DDL 実行先は先頭のデータソース)
                var dataSource = dataSources.FirstOrDefault(e => e.Name == options.DataSourceName) ?? dataSources.First();
                var result = MailSetupService.Run(designData, env.CurrentFileDirectory, options,
                    dataSource.DataSourceType, env.GetDbInfo(dataSource.Name));

                SetupResultWindow.ShowResult(env, dataSource, result);
            }
            catch (Exception ex)
            {
                MessageWindow.Show(ex.Message, "Error");
            }
        }
    }
}
