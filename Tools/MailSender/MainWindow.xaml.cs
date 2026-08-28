using Codeer.Mail.Gmail;
using MailSender.Services;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;
using System.Windows.Controls;

namespace MailSender
{
    public partial class MainWindow : FluentWindow
    {
        /// <summary>一覧の 1 行 (パッケージの項目 + 送信結果)。</summary>
        class Row : INotifyPropertyChanged
        {
            public MailPackageItem Item { get; }
            readonly Action _onSelectionChanged;
            string _status;
            bool _isFailed;
            bool _isSelected;
            bool _canSelect;

            public Row(MailPackageItem item, Action onSelectionChanged)
            {
                Item = item;
                _onSelectionChanged = onSelectionChanged;
                //初期値: 除外されていない行だけチェック。アドレスが無い行はチェックできない
                _isSelected = item.IsSendTarget;
                _canSelect = item.ToAddresses.Count > 0;
                _status = item.IsExcluded ? item.ExcludedText : "未送信";
            }

            /// <summary>送信するか (チェックボックス)。</summary>
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value) return;
                    _isSelected = value;
                    Notify(nameof(IsSelected));
                    _onSelectionChanged();
                }
            }

            /// <summary>チェックを操作できるか (アドレスが無い行と送信中は不可)。</summary>
            public bool CanSelect { get => _canSelect; set { _canSelect = value; Notify(nameof(CanSelect)); } }

            public string To => Item.To;
            public string Subject => Item.Subject;
            public bool IsExcluded => Item.IsExcluded;
            public bool IsFailed { get => _isFailed; set { _isFailed = value; Notify(nameof(IsFailed)); } }
            public string Status { get => _status; set { _status = value; Notify(nameof(Status)); } }

            public event PropertyChangedEventHandler? PropertyChanged;
            void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        AppSettings _settings = AppSettings.Load();
        readonly StoredAccounts _accounts = TokenStore.Load();
        StoredAccount? _token;
        MailPackage? _package;
        List<Row> _rows = new();
        CancellationTokenSource? _sendCancellation;
        CancellationTokenSource? _issueCancellation;
        bool _busy;

        public MainWindow(string? initialFile)
        {
            InitializeComponent();
            ApplyZoom(_settings.Zoom, save: false);
            RefreshAccount();
            RefreshSendButton();
            if (!string.IsNullOrEmpty(initialFile)) Loaded += (_, _) => LoadPackage(initialFile);
        }

        // ---------- アカウント (トークン)

        /// <summary>ComboBox の 1 行。</summary>
        record AccountEntry(StoredAccount Token, GmailClientSettings? Client)
        {
            public override string ToString()
                => Token.Email
                   + (Client == null ? " [発行クライアントが未登録]" : Client.IsWebClient ? " [ウェブ]" : string.Empty)
                   + $"  ({Token.IssuedAt:yyyy-MM-dd})";
        }

        /// <summary>
        /// このアカウントのトークンをリフレッシュできるクライアント (発行したもの)。設定に無ければ null = 送れない。
        /// 古い行 (ClientId 無し) はデスクトップ扱い。
        /// </summary>
        GmailClientSettings? ResolveClient(StoredAccount? token)
            => token == null ? null
             : string.IsNullOrEmpty(token.ClientId) ? (_settings.Gmail.Desktop.IsConfigured ? _settings.Gmail.Desktop : null)
             : _settings.Gmail.Find(token.ClientId);

        bool IsTokenUsable(StoredAccount? token) => ResolveClient(token) != null;

        /// <summary>選択中アカウントの説明 (発行クライアントと、各ボタンが何に効くか)。</summary>
        string DescribeSelected()
        {
            if (_accounts.Accounts.Count == 0) return "未登録 (「アカウントを追加」で Google アカウントの同意を取ります)";
            var client = ResolveClient(_token);
            if (client == null) return "このアカウントを発行した OAuth クライアントが設定にありません。設定に登録し直すか、「再発行」してください";
            return $"発行クライアント: {client.DisplayName}  —  再発行・トークンの書き出し・破棄はこのアカウント ({_token!.Email} / {client.DisplayName}) に対して行います";
        }

        void RefreshAccount()
        {
            _token = _accounts.Selected;
            _accounts.Select(_token);

            _refreshingAccounts = true;
            try
            {
                _accountsCombo.ItemsSource = _accounts.Accounts.Select(e => new AccountEntry(e, ResolveClient(e))).ToList();
                _accountsCombo.SelectedIndex = _token == null ? -1 : _accounts.Accounts.IndexOf(_token);
            }
            finally
            {
                _refreshingAccounts = false;
            }

            _accountsCombo.Visibility = _accounts.Accounts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            _accountText.Text = DescribeSelected();
            _accountsCombo.IsEnabled = !_busy;
            _reissueButton.IsEnabled = _token != null && !_busy;
            _revokeButton.IsEnabled = _token != null && !_busy;
            _exportButton.IsEnabled = _token != null && !_busy;
        }

        bool _refreshingAccounts;

        void OnAccountSelected(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshingAccounts) return;
            if (_accountsCombo.SelectedItem is AccountEntry entry)
            {
                _accounts.Select(entry.Token);
                TokenStore.Save(_accounts);
                _token = entry.Token;
                _accountText.Text = DescribeSelected();
                RefreshSendButton();
            }
        }

        /// <summary>
        /// Google アカウントを追加する (ブラウザでアカウントを選ぶ)。
        /// OAuth クライアントが 2 つ登録されていれば、どちらで発行するかをメニューで選ぶ (1 つならそのまま)。
        /// </summary>
        void OnAddAccount(object sender, RoutedEventArgs e)
        {
            var clients = _settings.Gmail.Configured.ToList();
            if (clients.Count <= 1)
            {
                _ = IssueTokenAsync(null, clients.FirstOrDefault());
                return;
            }
            var menu = new ContextMenu { PlacementTarget = _addButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var client in clients)
            {
                var item = new Wpf.Ui.Controls.MenuItem { Header = $"{client.DisplayName} で追加" };
                item.Click += (_, _) => _ = IssueTokenAsync(null, client);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        /// <summary>選択中アカウントのトークンを、そのアカウントを発行したクライアントで発行し直す。</summary>
        void OnReissueToken(object sender, RoutedEventArgs e)
        {
            if (_token == null) return;
            var client = ResolveClient(_token);
            if (client == null)
            {
                MessageBox.Show(this, "このアカウントを発行した OAuth クライアントが設定にありません。設定に登録し直してください。", "MailSender", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _ = IssueTokenAsync(_token.Email, client);
        }

        async Task IssueTokenAsync(string? loginHint, GmailClientSettings? client)
        {
            if (client == null)
            {
                MessageBox.Show(this, "先に「設定」で OAuth クライアント (ID とシークレット) を登録してください。", "MailSender", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SetBusy(true, "ブラウザで Google アカウントの同意を待っています... (ブラウザを閉じてしまったら「中止」)");
            //ブラウザを閉じられてもこちらには何も届かないので、「中止」で待機を抜けられるようにする (5 分で自動打ち切り)
            _issueCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            _cancelButton.IsEnabled = true;
            try
            {
                //Web 種別クライアントは登録済みの固定 URI で受ける (デスクトップ種別は任意ポート)
                using var receiver = new LoopbackCodeReceiver(client.IsWebClient ? client.RedirectUri : null);
                var pkce = GmailPkce.Create();
                var state = GmailApiClient.Base64Url(RandomNumberGenerator.GetBytes(16));
                //追加 = アカウント選択画面から。再発行 = そのアカウントを login_hint で指定
                var url = GmailOAuth.CreateAuthorizationUrl(client.ClientId, receiver.RedirectUri, GmailOAuth.SendWithEmailScope, state, pkce,
                    loginHint, selectAccount: loginHint == null);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

                var code = await receiver.WaitForCodeAsync(state, _issueCancellation.Token);
                if (code == null)
                {
                    _statusText.Text = _issueCancellation.IsCancellationRequested ? "発行を中止しました (中止ボタン、または 5 分経過)" : "同意はキャンセルされました";
                    return;
                }

                var response = await new GmailOAuth().ExchangeCodeAsync(client.ClientId, client.ClientSecret, code, receiver.RedirectUri, pkce);
                if (string.IsNullOrEmpty(response.RefreshToken))
                {
                    MessageBox.Show(this,
                        "Google からリフレッシュトークンが返されませんでした。\nGoogle アカウントの「セキュリティ > サードパーティ製のアプリとサービス」でこのアプリのアクセスを削除してから、もう一度発行してください。",
                        "MailSender", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (string.IsNullOrEmpty(response.Email))
                {
                    MessageBox.Show(this, "Google からメールアドレスが返されませんでした。もう一度発行してください。", "MailSender", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var token = new StoredAccount
                {
                    Provider = MailProviders.Gmail,
                    RefreshToken = response.RefreshToken,
                    Email = response.Email,
                    IssuedAt = DateTime.Now,
                    ClientId = client.ClientId,
                };
                var replaced = _accounts.Accounts.Any(a => a.Email == token.Email && a.ClientId == token.ClientId);
                _accounts.AddOrReplace(token);
                TokenStore.Save(_accounts);
                _statusText.Text = replaced ? $"{token.Email} のトークンを再発行しました" : $"{token.Email} を追加しました";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "トークンの発行に失敗しました", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _cancelButton.IsEnabled = false;
                _issueCancellation.Dispose();
                _issueCancellation = null;
                SetBusy(false, null);
            }
        }

        async void OnRevokeToken(object sender, RoutedEventArgs e)
        {
            if (_token == null) return;
            var target = _token;
            if (MessageBox.Show(this, $"{target.Email} を破棄しますか？\nGoogle 側でトークンを無効化し、この PC から削除します。", "MailSender",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            SetBusy(true, "トークンを破棄しています...");
            try
            {
                try
                {
                    await new GmailOAuth().RevokeAsync(target.RefreshToken);
                }
                catch (Exception ex)
                {
                    //Google 側の取り消しに失敗してもローカルは消す (既に無効な場合など)
                    AppendLog($"revoke failed: {ex.Message}");
                }
                _accounts.Remove(target);
                TokenStore.Save(_accounts);
                _statusText.Text = $"{target.Email} を破棄しました";
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        /// <summary>
        /// 選択中アカウントのリフレッシュトークンを Web アプリの Gmail.TokenSecret 用 JSON ({"refresh_token":"..."}) として保存する。
        /// 共通送信者 (システムアカウント) のトークンをサーバーへ持ち込む用途。平文なので扱いは慎重に。
        /// </summary>
        void OnExportToken(object sender, RoutedEventArgs e)
        {
            if (_token == null) return;
            if (MessageBox.Show(this,
                    $"{_token.Email} のリフレッシュトークンを平文の JSON ファイルに書き出します。\n" +
                    "このファイルを持つ人は、このアカウントの名義でメールを送れます。\n" +
                    "Web アプリの Gmail.TokenSecret に設定したら、ファイルは安全な場所に置くか削除してください。",
                    "MailSender", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;

            var dialog = new SaveFileDialog
            {
                Filter = "トークン JSON (*.json)|*.json",
                FileName = $"gmail_token_{_token.Email.Replace('@', '_')}.json",
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(new { refresh_token = _token.RefreshToken, email = _token.Email },
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dialog.FileName, json);
                _statusText.Text = $"トークンを書き出しました: {dialog.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "書き出しに失敗しました", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void OnSettings(object sender, RoutedEventArgs e)
        {
            new SettingsWindow(_settings) { Owner = this }.ShowDialog();
            //クライアントを差し替えると使えるアカウントが変わる
            RefreshAccount();
            RefreshSendButton();
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
                _rows = _package.Items.Select(e => new Row(e, RefreshSendButton)).ToList();
                _items.ItemsSource = _rows;
                var kind = _package.Kind == "bulk" ? "一斉送信" : "単発";
                var attachments = _package.AttachmentFiles.Count == 0 ? string.Empty : $"  添付: {string.Join(", ", _package.AttachmentFiles.Select(e => e.FileName))}";
                _packageText.Text = $"{_package.Title}  [{kind}]  作成: {_package.GeneratedAt}  {_package.Items.Count} 件{attachments}\n{path}";
                _statusText.Text = string.Empty;
                _progress.Value = 0;
                _progress.Visibility = Visibility.Collapsed;
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
                _ = ShowBodyAsync(row.Item.Body);
            }
            else
            {
                _bodyHeader.Text = "本文";
                _ = ShowBodyAsync(string.Empty);
            }
        }

        // ---------- 拡大縮小 (Ctrl + ホイール)

        const double MinZoom = 0.7, MaxZoom = 2.0, ZoomStep = 0.1;

        void OnPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;
            ApplyZoom(_settings.Zoom + (e.Delta > 0 ? ZoomStep : -ZoomStep), save: true);
            e.Handled = true;
        }

        void ApplyZoom(double zoom, bool save)
        {
            zoom = Math.Round(Math.Clamp(zoom, MinZoom, MaxZoom), 2);
            _settings.Zoom = zoom;
            _zoom.ScaleX = _zoom.ScaleY = zoom;
            //WebView2 は HwndHost なので LayoutTransform が中身に効かない → 自前のズームで追従
            if (_webViewReady) _bodyHtml.ZoomFactor = zoom;
            Title = zoom == 1.0 ? "MailSender" : $"MailSender ({zoom:P0})";
            if (save) _settings.Save();
        }

        // ---------- 本文プレビュー (HTML メールは WebView2、プレーンテキストは TextBox)

        /// <summary>
        /// 本文の描画中に外部へ一切アクセスさせないための CSP。開封検知用の追跡画像を踏んで「開封済み」を誤発火させない /
        /// 閲覧者の IP を配信先に渡さないため。Web 側のプレビュー HTML (MailPreview.html) と同じ方針。
        /// </summary>
        const string PreviewCsp = "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline';\">";

        bool _webViewReady;
        bool _webViewFailed;

        async Task ShowBodyAsync(string body)
        {
            var isHtml = _package?.IsBodyHtml == true;
            if (isHtml && !_webViewFailed) await EnsureWebViewAsync();
            var useWebView = isHtml && _webViewReady;

            _body.Visibility = useWebView ? Visibility.Collapsed : Visibility.Visible;
            _bodyHtmlFrame.Visibility = useWebView ? Visibility.Visible : Visibility.Collapsed;
            _bodyNote.Visibility = isHtml ? Visibility.Visible : Visibility.Collapsed;
            if (isHtml && !useWebView) _bodyNote.Text = "HTML メール: WebView2 ランタイムが無いためソースを表示しています";

            if (useWebView) _bodyHtml.NavigateToString(PreviewCsp + body);
            else _body.Text = body;
        }

        async Task EnsureWebViewAsync()
        {
            if (_webViewReady || _webViewFailed) return;
            try
            {
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, Path.Combine(AppSettings.DataFolder, "webview2"));
                await _bodyHtml.EnsureCoreWebView2Async(env);
                var core = _bodyHtml.CoreWebView2;
                core.Settings.IsScriptEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.AreDefaultScriptDialogsEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.Settings.IsPasswordAutosaveEnabled = false;

                //二重防御: CSP に加えて、ネットワークに出る要求は WebView2 側でも全部落とす
                core.AddWebResourceRequestedFilter("*", Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (_, args) =>
                {
                    if (IsLocalPreviewUri(args.Request.Uri)) return;
                    args.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked by MailSender preview", string.Empty);
                };
                //リンクのクリック等で外へ遷移しない / 新しいウィンドウも開かない
                core.NavigationStarting += (_, args) => { if (!IsLocalPreviewUri(args.Uri)) args.Cancel = true; };
                core.NewWindowRequested += (_, args) => args.Handled = true;
                _webViewReady = true;
                _bodyHtml.ZoomFactor = _settings.Zoom;
            }
            catch (Exception ex)
            {
                //WebView2 ランタイム未導入など。ソース表示にフォールバック
                _webViewFailed = true;
                AppendLog($"WebView2 unavailable: {ex.Message}");
            }
        }

        //NavigateToString の内容は about:blank / data: として読み込まれる。それ以外 (http/https/file 等) は外部
        static bool IsLocalPreviewUri(string uri)
            => uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase);

        // ---------- 送信

        List<Row> SelectedRows => _rows.Where(r => r.IsSelected && r.Item.ToAddresses.Count > 0).ToList();

        void RefreshSendButton()
        {
            var targets = SelectedRows.Count;
            _sendButton.IsEnabled = !_busy && IsTokenUsable(_token) && targets > 0;
            _sendButton.Content = targets > 0 ? $"送信 ({targets} 件)" : "送信";
            _selectAllButton.IsEnabled = !_busy && _rows.Count > 0;
            _clearAllButton.IsEnabled = !_busy && _rows.Count > 0;
        }

        void OnSelectAll(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) if (r.CanSelect) r.IsSelected = true;
        }

        void OnClearAll(object sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) r.IsSelected = false;
        }

        async void OnSend(object sender, RoutedEventArgs e)
        {
            if (_package == null || _token == null) return;
            var targets = SelectedRows;
            if (targets.Count == 0) return;
            if (MessageBox.Show(this, $"チェックした {targets.Count} 件を送信します。\n差出人: {_token.Email}", "MailSender",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

            foreach (var r in _rows)
            {
                r.IsFailed = false;
                r.Status = targets.Contains(r) ? "待機中" : "スキップ";
            }
            _progress.Maximum = targets.Count;
            _progress.Value = 0;
            _progress.Visibility = Visibility.Visible;
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
                await new SendService(ResolveClient(_token)!, _token.RefreshToken).SendAsync(_package, targets.Select(r => r.Item), progress, _sendCancellation.Token);
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
            if (_issueCancellation != null)
            {
                //トークン発行の待機を打ち切る
                _issueCancellation.Cancel();
                _cancelButton.IsEnabled = false;
                return;
            }
            _sendCancellation?.Cancel();
            _statusText.Text = "中止しています (送信中の 1 通が終わったら止まります)...";
            _cancelButton.IsEnabled = false;
        }

        void SetBusy(bool busy, string? status)
        {
            _busy = busy;
            if (status != null) _statusText.Text = status;
            _addButton.IsEnabled = !busy;
            _settingsButton.IsEnabled = !busy;
            _openButton.IsEnabled = !busy;
            //送信中はチェックを触らせない
            foreach (var r in _rows) r.CanSelect = !busy && r.Item.ToAddresses.Count > 0;
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
