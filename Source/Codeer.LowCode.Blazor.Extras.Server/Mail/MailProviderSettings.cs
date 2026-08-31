namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// SMTP (MailKit) の設定。appsettings のセクション名はアプリが決める (テンプレートの対応表が読む。既定は "Smtp")。
    /// </summary>
    public class SmtpSettings
    {
        /// <summary>差出人アドレス (システム送信者。メールは常にこのアドレスから送られる)。</summary>
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

    /// <summary>
    /// Microsoft Graph (sendMail・アプリケーション権限 Mail.Send) の設定。
    /// <see cref="ClientSecret"/> があればクライアントクレデンシャル (TenantId / ClientId / ClientSecret)、
    /// 空なら DefaultAzureCredential (App Service 等の Managed Identity、ローカルは Azure CLI / Visual Studio のログイン)。
    /// Managed Identity なら設定にシークレットを持たなくてよい (Mail.Send のアプリロールを MI のサービスプリンシパルに付与する)。
    /// </summary>
    public class GraphApiSettings
    {
        /// <summary>差出人アドレス。アプリケーション権限 Mail.Send はテナント内の任意ユーザーとして送れる。</summary>
        public string SenderMailAddress { get; set; } = string.Empty;

        /// <summary>差出人の表示名。</summary>
        public string SenderDisplayName { get; set; } = string.Empty;

        /// <summary>一斉送信1回の件数上限。超過はエラー (黙って切り詰めない)。</summary>
        public int MaxBulkCount { get; set; } = 10000;

        /// <summary>テナント ID。クライアントクレデンシャルでは必須。DefaultAzureCredential では任意 (ローカルログインが別テナント既定のときに指定)。</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>アプリ登録のクライアント ID。クライアントクレデンシャルでは必須。Managed Identity では不要。</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>アプリ登録のクライアントシークレット。空なら DefaultAzureCredential で認証する。appsettings.Development.json / 環境変数 (GraphApi__ClientSecret) に置く。</summary>
        public string ClientSecret { get; set; } = string.Empty;
    }

    /// <summary>SendGrid (v3 mail/send) の設定。</summary>
    public class SendGridSettings
    {
        /// <summary>差出人アドレス (SendGrid で検証済みのもの。Single Sender Verification またはドメイン認証)。</summary>
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

        /// <summary>
        /// 一斉送信1回の件数上限。Gmail の送信上限は低い (Workspace = 1 ユーザー 1 日 2,000 通、無料 Gmail = 500 通。
        /// 超えると残りが quota exceeded で失敗) ので既定は 500。大量配信は配信サービス系のインフラで行う。
        /// </summary>
        public int MaxBulkCount { get; set; } = 500;

        /// <summary>
        /// サービスアカウントの JSON キー (ドメイン全体の委任モード)、または
        /// OAuth クライアントの client_secret JSON (installed/web = ユーザー同意モード)。
        /// 値が ".json" で終わればファイルパス、それ以外は JSON 文字列そのもの
        /// (環境変数 Gmail__ClientSecret 等に直接入れられる。ファイルを置かなくてよい)。
        /// </summary>
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// ユーザー同意モードで使う、同意で得たリフレッシュトークン。
        /// 値が ".json" で終わればファイルパス、それ以外は JSON ({"refresh_token":"..."}) かトークン文字列そのもの
        /// (環境変数 Gmail__TokenSecret 等に直接入れられる)。
        /// システム送信者の位置づけ (メールは常にこのアカウントから送られる)。
        /// </summary>
        public string TokenSecret { get; set; } = string.Empty;
    }
}
