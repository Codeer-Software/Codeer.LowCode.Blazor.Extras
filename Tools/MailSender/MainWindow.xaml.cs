using Codeer.Mail.Gmail;
using MailSender.Services;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;

namespace MailSender
{
    public partial class MainWindow : Window
    {
        /// <summary>一覧の 1 行 (パッケージの項目 + 送信結果)。</summary>
        class Row : INotifyPropertyChanged
        {
            public MailPackageItem Item { get; }
            string _status;
            bool _isFailed;

            public Row(MailPackageItem item)
            {
                Item = item;
                _status = item.IsExcluded ? $"除外 ({item.ExcludedText})" : "未送信";
            }

            public string To => Item.To;
            public string Subject => Item.Subject;
            public bool IsExcluded => Item.IsExcluded;
            public bool IsFailed { get => _isFailed; set { _isFailed = value; Notify(nameof(IsFailed)); } }
            public string Status { get => _status; set { _status = value; Notify(nameof(Status)); } }

            public event PropertyChangedEventHandler? PropertyChanged;
            void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        AppSettings _settings = AppSettings.Load();
        StoredToken? _token = TokenStore.Load();
        MailPackage? _package;
        List<Row> _rows = new();
        CancellationTokenSource? _sendCancellation;
        bool _busy;

        public MainWindow(string? initialFile)
        {
            InitializeComponent();
            RefreshAccount();
            RefreshSendButton();
            if (!string.IsNullOrEmpty(initialFile)) Loaded += (_, _) => LoadPackage(initialFile);
        }

        // ---------- アカウント (トークン)

        void RefreshAccount()
        {
            _accountText.Text = _token == null
                ? "未発行 (「トークンを発行」で Google アカウントの同意を取ります)"
                : $"{_token.Email}  (発行: {_token.IssuedAt:yyyy-MM-dd HH:mm})";
            _revokeButton.IsEnabled = _token != null && !_busy;
            _issueButton.Content = _token == null ? "トークンを発行" : "トークンを再発行";
        }

        async void OnIssueToken(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_settings.ClientId))
            {
                MessageBox.Show(this, "先に「設定」で OAuth クライアント ID を入力してください。", "MailSender", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SetBusy(true, "ブラウザで Google アカウントの同意を待っています...");
            try
            {
                using var receiver = new LoopbackCodeReceiver();
                var pkce = GmailPkce.Create();
                var state = GmailApiClient.Base64Url(RandomNumberGenerator.GetBytes(16));
                var url = GmailOAuth.CreateAuthorizationUrl(_settings.ClientId, receiver.RedirectUri, GmailOAuth.SendWithEmailScope, state, pkce, _token?.Email);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

                //5 分待って戻ってこなければあきらめる
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                var code = await receiver.WaitForCodeAsync(state, timeout.Token);
                if (code == null)
                {
                    _statusText.Text = "同意はキャンセルされました";
                    return;
                }

                var response = await new GmailOAuth().ExchangeCodeAsync(_settings.ClientId, null, code, receiver.RedirectUri, pkce);
                if (string.IsNullOrEmpty(response.RefreshToken))
                {
                    MessageBox.Show(this,
                        "Google からリフレッシュトークンが返されませんでした。\nGoogle アカウントの「セキュリティ > サードパーティ製のアプリとサービス」でこのアプリのアクセスを削除してから、もう一度発行してください。",
                        "MailSender", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _token = new StoredToken { RefreshToken = response.RefreshToken, Email = response.Email ?? string.Empty, IssuedAt = DateTime.Now };
                TokenStore.Save(_token);
                _statusText.Text = "トークンを発行しました";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "トークンの発行に失敗しました", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false, null);
                RefreshAccount();
            }
        }

        async void OnRevokeToken(object sender, RoutedEventArgs e)
        {
            if (_token == null) return;
            if (MessageBox.Show(this, $"{_token.Email} のトークンを破棄しますか？\nGoogle 側で無効化し、この PC から削除します。", "MailSender",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            SetBusy(true, "トークンを破棄しています...");
            try
            {
                try
                {
                    await new GmailOAuth().RevokeAsync(_token.RefreshToken);
                }
                catch (Exception ex)
                {
                    //Google 側の取り消しに失敗してもローカルは消す (既に無効な場合など)
                    AppendLog($"revoke failed: {ex.Message}");
                }
                TokenStore.Delete();
                _token = null;
                _statusText.Text = "トークンを破棄しました";
            }
            finally
            {
                SetBusy(false, null);
                RefreshAccount();
            }
        }

        void OnSettings(object sender, RoutedEventArgs e)
        {
            new SettingsWindow(_settings) { Owner = this }.ShowDialog();
        }

        // ---------- パッケージ

        void OnOpen(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "プレビュー HTML (*.html)|*.html;*.htm|すべてのファイル (*.*)|*.*" };
            if (dialog.ShowDialog(this) == true) LoadPackage(dialog.FileName);
        }

        void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = !_busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            if (_busy) return;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) LoadPackage(files[0]);
        }

        void LoadPackage(string path)
        {
            try
            {
                _package = MailPackage.Load(path);
                _rows = _package.Items.Select(e => new Row(e)).ToList();
                _items.ItemsSource = _rows;
                var kind = _package.Kind == "bulk" ? "一斉送信" : "単発";
                var attachments = _package.AttachmentFiles.Count == 0 ? string.Empty : $"  添付: {string.Join(", ", _package.AttachmentFiles.Select(e => e.FileName))}";
                _packageText.Text = $"{_package.Title}  [{kind}]  作成: {_package.GeneratedAt}  送信対象 {_package.Items.Count(e => e.IsSendTarget)} / {_package.Items.Count} 件{attachments}\n{path}";
                _statusText.Text = string.Empty;
                _progress.Value = 0;
                if (_rows.Count > 0) _items.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                _package = null;
                _rows = new();
                _items.ItemsSource = null;
                _packageText.Text = "ファイルを開くか、ここにドロップしてください";
                _body.Text = string.Empty;
                MessageBox.Show(this, ex.Message, "開けませんでした", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            RefreshSendButton();
        }

        void OnItemSelected(object sender, SelectionChangedEventArgs e)
        {
            if (_items.SelectedItem is Row row)
            {
                var cc = row.Item.Cc.Count == 0 ? string.Empty : $"\nCc: {string.Join(", ", row.Item.Cc)}";
                var bcc = row.Item.Bcc.Count == 0 ? string.Empty : $"\nBcc: {string.Join(", ", row.Item.Bcc)}";
                _bodyHeader.Text = $"件名: {row.Subject}\n宛先: {row.To}{cc}{bcc}";
                _body.Text = row.Item.Body;
            }
            else
            {
                _bodyHeader.Text = "本文";
                _body.Text = string.Empty;
            }
        }

        // ---------- 送信

        void RefreshSendButton()
        {
            var targets = _package?.Items.Count(e => e.IsSendTarget) ?? 0;
            _sendButton.IsEnabled = !_busy && _token != null && targets > 0;
            _sendButton.Content = targets > 0 ? $"送信 ({targets} 件)" : "送信";
        }

        async void OnSend(object sender, RoutedEventArgs e)
        {
            if (_package == null || _token == null) return;
            var targets = _rows.Where(r => r.Item.IsSendTarget).ToList();
            if (MessageBox.Show(this, $"{targets.Count} 件送信します。\n差出人: {_token.Email}", "MailSender",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

            foreach (var r in targets) { r.Status = "待機中"; r.IsFailed = false; }
            _progress.Maximum = targets.Count;
            _progress.Value = 0;
            _sendCancellation = new CancellationTokenSource();
            SetBusy(true, $"送信中... 0 / {targets.Count}");
            _cancelButton.IsEnabled = true;

            var done = 0;
            var success = 0;
            var progress = new Progress<SendItemResult>(result =>
            {
                var row = _rows.First(r => ReferenceEquals(r.Item, result.Item));
                row.Status = result.IsSuccess == true ? "成功" : $"失敗: {result.Error}";
                row.IsFailed = result.IsSuccess != true;
                done++;
                if (result.IsSuccess == true) success++;
                _progress.Value = done;
                _statusText.Text = $"送信中... {done} / {targets.Count}";
            });
            try
            {
                await new SendService(_settings.ClientId, _token.RefreshToken).SendAsync(_package, progress, _sendCancellation.Token);
                _statusText.Text = $"完了: 成功 {success} 件 / 失敗 {done - success} 件 (ログ: {SendService.LogFolder})";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "送信に失敗しました", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _cancelButton.IsEnabled = false;
                _sendCancellation = null;
                SetBusy(false, null);
            }
        }

        void OnCancel(object sender, RoutedEventArgs e)
        {
            _sendCancellation?.Cancel();
            _statusText.Text = "中止しています (送信中の 1 通が終わったら止まります)...";
            _cancelButton.IsEnabled = false;
        }

        void SetBusy(bool busy, string? status)
        {
            _busy = busy;
            if (status != null) _statusText.Text = status;
            _issueButton.IsEnabled = !busy;
            _settingsButton.IsEnabled = !busy;
            _openButton.IsEnabled = !busy;
            RefreshAccount();
            RefreshSendButton();
        }

        static void AppendLog(string message)
        {
            try
            {
                Directory.CreateDirectory(SendService.LogFolder);
                File.AppendAllText(Path.Combine(SendService.LogFolder, $"{DateTime.Now:yyyyMMdd}.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
