using Azure.AI.OpenAI;
using Codeer.LowCode.Blazor.Components.AppParts.Loading;
using Codeer.LowCode.Blazor.Designer;
using Codeer.LowCode.Blazor.Designer.Standard;
using Codeer.LowCode.Blazor.Extras.Designer;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.ClientModel;
using System.Configuration;
using System.Windows;

namespace Extras.Designer
{
    public partial class App : DesignerApp
    {
        //AZURE_OPENAI_* の3つが揃っているときだけ Azure OpenAI の IChatClient ファクトリを返す(欠けていればAIチャット無効)。
        static Func<IChatClient>? CreateAzureOpenAIChatClientFactory()
        {
            var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_ENDPOINT");
            var key = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
            var model = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_MODEL");
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(model)) return null;

            return () => new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(key))
                .GetChatClient(model)
                .AsIChatClient();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            //load dll. ProCodeManager 等のロード済みアセンブリスキャンより前に Extras.Client.Shared をロードしておく
            //(これが無いとプロコードコンポーネント/モジュールがデザインチェックで「存在しません」になる)
            typeof(global::Extras.Client.Shared.Services.AppInfoService).ToString();

            //プロジェクトテンプレートと claude-workspace verb は headless CLI からも参照されるため、
            //headless 分岐が走る base.OnStartup より前に登録する (冪等)。
            DesignerStandard.SetupHeadless();
            ExtrasDesignerInitializer.Initialize(BlazorRuntime);

            Codeer.LowCode.Blazor.License.LicenseManager.IsAutoUpdate =
                bool.TryParse(ConfigurationManager.AppSettings["IsLicenseAutoUpdate"], out var val) ? val : true;

            //アプリ固有: サービス・スクリプト型を登録
            //(Extras のスクリプトオブジェクト群は ExtrasDesignerInitializer.Initialize が登録する)
            Services.AddSingleton<IDbAccessorFactory, DbAccessorFactory>();
            ScriptRuntimeTypeManager.AddService(new LoadingService());
            ScriptRuntimeTypeManager.AddType<LoadingService.LoadingScope>();

            BlazorRuntime.InstallBundleCss("Extras.Client.Shared");

            base.OnStartup(e);

            MainWindow.Title = "Extras";

            //標準実装一式 (アイコン候補 / プロジェクトテンプレート / ツールメニュー / AIチャット) を登録。
            //一部だけ使いたい場合は DesignerStandard.Setup の中身と同じコードを個別に書ける
            //(StandardTemplates / StandardMenus / StandardIcons / DesignerChatRegistration)。
            //AIチャットのモデルはライブラリが IChatClient 抽象しか知らないため、
            //プロバイダ選択(Azure OpenAI)と認証情報はアプリ側のここで持ち、ファクトリとして渡す。
            DesignerStandard.Setup(DesignerEnvironment, new DesignerStandardOptions
            {
                CreateAiChatClient = CreateAzureOpenAIChatClientFactory(),
            });

            DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                $"An unhandled exception occurred:  {e.Exception.Message}{Environment.NewLine}{e.Exception.StackTrace}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
