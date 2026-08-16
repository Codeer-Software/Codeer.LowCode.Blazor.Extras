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
            AddMailHistorySetup(env);
        }

        /// <summary>Tools &gt; 承認フローのセットアップ。承認モジュール群の生成と申請書への結線。</summary>
        public static void AddApprovalFlowSetup(DesignerEnvironment env)
            => env.AddMainMenu(() => RunApprovalSetup(env), "Tools", Properties.Resources.SetupMenuApprovalFlow);

        /// <summary>Tools &gt; メール履歴モジュールの生成。</summary>
        public static void AddMailHistorySetup(DesignerEnvironment env)
            => env.AddMainMenu(() => RunMailHistorySetup(env), "Tools", Properties.Resources.SetupMenuMailHistory);

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

                SetupResultWindow.ShowResult(env, dataSource, result,
                    string.IsNullOrEmpty(options.TargetModuleName) ? null : options.TargetModuleName);
            }
            catch (Exception ex)
            {
                MessageWindow.Show(ex.Message, "Error");
            }
        }

        static void RunMailHistorySetup(DesignerEnvironment env)
        {
            if (string.IsNullOrEmpty(env.CurrentFileDirectory)) return;
            try
            {
                var designData = env.GetDesignData();
                var dataSources = env.GetDesignerSettings().DataSources;

                var options = MailHistorySetupWindow.ShowDialog(designData, dataSources.Select(e => e.Name).ToList());
                if (options == null) return;

                var dataSource = dataSources.First(e => e.Name == options.DataSourceName);
                var result = MailHistorySetupService.Run(designData, env.CurrentFileDirectory, options,
                    dataSource.DataSourceType, env.GetDbInfo(dataSource.Name));

                SetupResultWindow.ShowResult(env, dataSource, result, null);
            }
            catch (Exception ex)
            {
                MessageWindow.Show(ex.Message, "Error");
            }
        }
    }
}
