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
        GmailClientSettings _gmail;

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            _gmail = settings.Gmail;
            _dataFolderText.Text = AppSettings.DataFolder;
            Refresh();
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

        void Refresh()
        {
            _clientIdText.Text = _gmail.IsConfigured ? _gmail.ClientId : "(未設定)";
            _secretState.Text = !_gmail.IsConfigured ? string.Empty
                : string.IsNullOrEmpty(_gmail.ClientSecret) ? "client_secret: なし (PKCE のみ)" : "client_secret: あり";
        }

        void OnBrowse(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "OAuth クライアント JSON (*.json)|*.json|すべてのファイル (*.*)|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                _gmail = GmailClientSettings.FromClientSecretJson(dialog.FileName);
                Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "読み込めませんでした", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void OnOk(object sender, RoutedEventArgs e)
        {
            _settings.Gmail = _gmail;
            _settings.Save();
            DialogResult = true;
        }
    }
}
