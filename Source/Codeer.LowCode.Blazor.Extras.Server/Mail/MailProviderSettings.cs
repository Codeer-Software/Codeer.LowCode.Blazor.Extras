namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// SMTP (MailKit) の設定。appsettings のセクション名はアプリが決める (テンプレートの対応表が読む。既定は "Smtp")。
    /// </summary>
    //Not published yet: only Gmail has been verified by real sending. Keep internal until verified, then make public and wire into MailSenderTable / SystemConfig / Program.cs
    internal class SmtpSettings
    {
        /// <summary>差出人アドレス (「自分を差出人にする」がOFFのときの差出人)。</summary>
        public string SenderMailAddress { get; set; } = string.Empty;

        /// <summary>差出人の表示名。</summary>
        public string SenderDisplayName { get; set; } = string.Empty;

        /// <summary>一斉送信1回の件数上限。超過はエラー (黙って切り詰めない)。</summary>
        public int MaxBulkCount { get; set; } = 10000;

        public string Host { get; set; } = string.Empty;
        public string Port { get; set; } = string.Empty;
        public string SSL { get; set; } = string.Empty;

        /// <summary>SMTP 認証ユーザー。空なら <see cref="SenderMailAddress"/> を使う。</summary>
        public string UserName { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;
    }

    /// <summary>Microsoft Graph (sendMail・アプリケーション権限) の設定。</summary>
    //Not published yet: only Gmail has been verified by real sending. Keep internal until verified, then make public and wire into MailSenderTable / SystemConfig / Program.cs
    internal class GraphApiSettings
    {
        /// <summary>差出人アドレス。アプリケーション権限 Mail.Send はテナント内の任意ユーザーとして送れる。</summary>
        public string SenderMailAddress { get; set; } = string.Empty;

        /// <summary>差出人の表示名。</summary>
        public string SenderDisplayName { get; set; } = string.Empty;

        /// <summary>一斉送信1回の件数上限。超過はエラー (黙って切り詰めない)。</summary>
        public int MaxBulkCount { get; set; } = 10000;

        public string TenantId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;

        /// <summary>アプリ登録のクライアントシークレット。</summary>
        public string ClientSecret { get; set; } = string.Empty;
    }

    /// <summary>SendGrid (v3 mail/send) の設定。</summary>
    //Not published yet: only Gmail has been verified by real sending. Keep internal until verified, then make public and wire into MailSenderTable / SystemConfig / Program.cs
    internal class SendGridSettings
    {
        /// <summary>差出人アドレス (ドメイン認証済みのもの)。</summary>
        public string SenderMailAddress { get; set; } = string.Empty;

        /// <summary>差出人の表示名。</summary>
        public string SenderDisplayName { get; set; } = string.Empty;

        /// <summary>一斉送信1回の件数上限。超過はエラー (黙って切り詰めない)。</summary>
        public int MaxBulkCount { get; set; } = 10000;

        public string ApiKey { get; set; } = string.Empty;
    }

    /// <summary>
    /// Gmail API の設定。<see cref="ClientSecret"/> の JSON の種類で認証モードが決まる
    /// (サービスアカウントキー = ドメイン全体の委任モード / OAuth クライアント = ユーザー同意モード)。
    /// </summary>
    public class GmailSettings
    {
        /// <summary>差出人アドレス (委任モードの委任ユーザー / 同意モードのシステム送信者)。</summary>
        public string SenderMailAddress { get; set; } = string.Empty;

        /// <summary>差出人の表示名。</summary>
        public string SenderDisplayName { get; set; } = string.Empty;

        /// <summary>一斉送信1回の件数上限。Gmail の送信上限は低いので小さめにする。</summary>
        public int MaxBulkCount { get; set; } = 10000;

        /// <summary>
        /// サービスアカウントの JSON キー (ドメイン全体の委任モード)、または
        /// OAuth クライアントの client_secret JSON (installed/web = ユーザー同意モード)。
        /// どちらもファイルパスか JSON 文字列そのもの。
        /// </summary>
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// ユーザー同意モードで使う、同意で得たリフレッシュトークンの JSON
        /// ({"refresh_token":"..."}。ファイルパスか JSON 文字列そのもの)。
        /// システム送信者 (ユーザートークン未登録時のフォールバック) の位置づけ。
        /// </summary>
        public string TokenSecret { get; set; } = string.Empty;

        /// <summary>
        /// GmailTokenField をDBに保存するときの暗号化鍵 (AES-GCM)。
        /// 長さ自由の文字列を SHA-256 で 256bit 鍵に畳む。
        /// **未設定のままトークンを保存しようとするとエラーになる** (平文で保存しない)。
        /// 環境変数で上書き可 (例 Gmail__TokenEncryptionKey)。リポジトリやデザインファイルには置かないこと。
        /// </summary>
        public string TokenEncryptionKey { get; set; } = string.Empty;
    }
}
