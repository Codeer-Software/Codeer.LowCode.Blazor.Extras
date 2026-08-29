using MailSender.Services;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace MailSender
{
    public partial class SettingsWindow : FluentWindow
    {
        readonly AppSettings _settings;
        string _webRedirectUri = GmailClientSettings.DefaultWebRedirectUri;
        bool _loading;

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            _dataFolderText.Text = AppSettings.DataFolder;
            _loading = true;
            try
            {
                Load(settings.Gmail.Desktop, _desktopId, _desktopSecret);
                Load(settings.Gmail.Web, _webId, _webSecret);
                _webRedirectUri = settings.Gmail.Web.RedirectUri;
                _graphClientId.Text = settings.GraphApi.ClientId;
                _graphTenantId.Text = settings.GraphApi.TenantId;
            }
            finally
            {
                _loading = false;
            }
            Refresh();
        }

        static void Load(GmailClientSettings client, TextBox id, TextBox secret)
        {
            id.Text = client.ClientId;
            secret.Text = client.ClientSecret;
        }

        /// <summary>画面の入力から設定値を作る。</summary>
        GmailSettings Collect() => new()
        {
            Desktop = new GmailClientSettings { IsWebClient = false, ClientId = _desktopId.Text.Trim(), ClientSecret = _desktopSecret.Text.Trim() },
            Web = new GmailClientSettings { IsWebClient = true, ClientId = _webId.Text.Trim(), ClientSecret = _webSecret.Text.Trim(), RedirectUri = _webRedirectUri },
        };

        GraphSettings CollectGraph() => new()
        {
            ClientId = _graphClientId.Text.Trim(),
            TenantId = string.IsNullOrWhiteSpace(_graphTenantId.Text) ? Codeer.Mail.Graph.GraphOAuth.DefaultTenant : _graphTenantId.Text.Trim(),
        };

        void Refresh()
        {
            //XAML 読込中はまだ他のコントロールが無い
            if (_loading || !IsInitialized || _webNote == null) return;
            var gmail = Collect();
            _desktopExport.IsEnabled = gmail.Desktop.IsConfigured;
            _webExport.IsEnabled = gmail.Web.IsConfigured;
            _webNote.Message = "Google Cloud のこのクライアントの「承認済みのリダイレクト URI」に次の値を登録してください:\n" + _webRedirectUri + "\n" +
                               "ウェブ種別のシークレットは Web アプリのサーバーにだけ置き、配布しないでください。";
        }

        void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Refresh();

        static bool IsWeb(object sender) => (sender as FrameworkElement)?.Tag as string == "web";

        void OnBrowse(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "OAuth クライアント JSON (*.json)|*.json|すべてのファイル (*.*)|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var client = GmailClientSettings.FromClientSecretJson(dialog.FileName);
                if (client.IsWebClient != IsWeb(sender))
                {
                    MessageBox.Show(this,
                        $"この JSON は「{client.DisplayName}」種別のクライアントです。{(client.IsWebClient ? "ウェブ" : "デスクトップ")}側の「JSON を読み込む」で読み込んでください。",
                        "MailSender", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (client.IsWebClient)
                {
                    Load(client, _webId, _webSecret);
                    _webRedirectUri = client.RedirectUri;
                }
                else
                {
                    Load(client, _desktopId, _desktopSecret);
                }
                Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "読み込めませんでした", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>Web アプリの Gmail.ClientSecret に置く JSON (Google の client_secret.json と同じ形) を書き出す。</summary>
        void OnExportJson(object sender, RoutedEventArgs e)
        {
            var gmail = Collect();
            var client = IsWeb(sender) ? gmail.Web : gmail.Desktop;
            if (!client.IsConfigured) return;
            var dialog = new SaveFileDialog
            {
                Filter = "OAuth クライアント JSON (*.json)|*.json",
                FileName = client.IsWebClient ? "client_secret_web.json" : "client_secret_desktop.json",
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(dialog.FileName, client.ToClientSecretJson());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "書き出しに失敗しました", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void OnOpenDataFolder(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.DataFolder);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppSettings.DataFolder}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "開けませんでした", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void OnOk(object sender, RoutedEventArgs e)
        {
            var gmail = Collect();
            //よくある間違い: クライアントの「名前」を入れてしまう。ID は 数字-英数字.apps.googleusercontent.com
            var odd = gmail.Configured.FirstOrDefault(c => !c.ClientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase));
            if (odd != null &&
                MessageBox.Show(this,
                    $"{odd.DisplayName}のクライアント ID は通常「数字-英数字.apps.googleusercontent.com」の形式です。\n" +
                    "Google Cloud の「認証情報」に表示されるクライアントの名前ではなく、「クライアント ID」の値を入れてください。\n\nこのまま保存しますか？",
                    "MailSender", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return;
            }
            var graph = CollectGraph();
            if (graph.IsConfigured && !Guid.TryParse(graph.ClientId, out _) &&
                MessageBox.Show(this,
                    "Microsoft 365 のアプリケーション (クライアント) ID は通常 GUID (00000000-0000-...) の形式です。\n" +
                    "Entra ID のアプリ登録の「概要」に表示される「アプリケーション (クライアント) ID」の値を入れてください。\n\nこのまま保存しますか？",
                    "MailSender", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return;
            }
            _settings.Gmail = gmail;
            _settings.GraphApi = graph;
            _settings.Save();
            DialogResult = true;
        }
    }
}
