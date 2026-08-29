using Codeer.Mail.Smtp;
using MailSender.Services;
using System.Windows;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;

namespace MailSender
{
    /// <summary>SMTP アカウントの追加・編集。結果は <see cref="Account"/>。</summary>
    public partial class SmtpAccountWindow : FluentWindow
    {
        public StoredAccount? Account { get; private set; }
        bool _loaded;

        public SmtpAccountWindow(StoredAccount? existing)
        {
            InitializeComponent();
            var s = existing?.Smtp ?? new SmtpAccountSettings();
            _email.Text = s.Email;
            _displayName.Text = s.DisplayName;
            _host.Text = s.Host;
            _port.Text = s.Port.ToString();
            _userName.Text = s.UserName;
            _password.Password = s.Password;
            _encryption.SelectedIndex = s.Encryption switch { SmtpEncryption.SslOnConnect => 1, SmtpEncryption.None => 2, _ => 0 };
            _loaded = true;
            Refresh();
        }

        SmtpAccountSettings Collect() => new()
        {
            Email = _email.Text.Trim(),
            DisplayName = _displayName.Text.Trim(),
            Host = _host.Text.Trim(),
            Port = int.TryParse(_port.Text.Trim(), out var p) ? p : 0,
            Encryption = _encryption.SelectedIndex switch { 1 => SmtpEncryption.SslOnConnect, 2 => SmtpEncryption.None, _ => SmtpEncryption.StartTls },
            UserName = _userName.Text.Trim(),
            Password = _password.Password,
        };

        string? Validate(SmtpAccountSettings s)
        {
            if (string.IsNullOrEmpty(s.Email) || !s.Email.Contains('@')) return "差出人のメールアドレスを入力してください。";
            if (string.IsNullOrEmpty(s.Host)) return "SMTP サーバーのホストを入力してください。";
            if (s.Port <= 0 || s.Port > 65535) return "ポートは 1〜65535 の数値です。";
            return null;
        }

        void Refresh()
        {
            if (!_loaded) return;
            var ok = Validate(Collect()) == null;
            _okButton.IsEnabled = ok;
            _testButton.IsEnabled = ok;
        }

        void OnChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Refresh();

        //暗号化を切り替えたら、まだ既定のままのポートを慣例値に合わせる
        void OnEncryptionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            if (_port.Text is "587" or "465" or "25")
                _port.Text = _encryption.SelectedIndex switch { 1 => "465", 2 => "25", _ => "587" };
            Refresh();
        }

        async void OnTest(object sender, RoutedEventArgs e)
        {
            var settings = Collect();
            _testButton.IsEnabled = false;
            _testResult.Text = "接続しています...";
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await using var smtp = new SmtpSender(settings);
                await smtp.ConnectAsync(timeout.Token);
                _testResult.Text = settings.UseAuthentication ? "接続と認証に成功しました" : "接続に成功しました (認証なし)";
            }
            catch (OperationCanceledException)
            {
                _testResult.Text = "タイムアウトしました (ホスト・ポート・暗号化を確認してください)";
            }
            catch (Exception ex)
            {
                _testResult.Text = ex.Message;
            }
            finally
            {
                _testButton.IsEnabled = true;
            }
        }

        void OnOk(object sender, RoutedEventArgs e)
        {
            var settings = Collect();
            if (Validate(settings) is { } error)
            {
                MessageBox.Show(this, error, "MailSender", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Account = new StoredAccount
            {
                Provider = MailProviders.Smtp,
                Email = settings.Email,
                DisplayName = settings.DisplayName,
                IssuedAt = DateTime.Now,
                Smtp = settings,
            };
            DialogResult = true;
        }
    }
}
