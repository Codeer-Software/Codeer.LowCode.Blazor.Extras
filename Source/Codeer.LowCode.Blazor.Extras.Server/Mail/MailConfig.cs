namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// "Mail" section of appsettings.json. Named senders with different infrastructures
    /// (SMTP / Microsoft Graph / SendGrid / Gmail) plus app-wide options.
    /// </summary>
    public class MailConfig
    {
        /// <summary>
        /// ★デバッグ専用 (本番では設定しない)。設定すると、全メールが実際の宛先の代わりに
        /// このアドレスへリダイレクトされる。本番データのコピーで動く開発・ステージング環境で、
        /// 実在の宛先へ誤送信する事故を遮断するためのもの。
        /// 元の宛先は X-CLB-Original-To ヘッダに記録される。一斉送信は先頭10通に切り詰められる。
        /// </summary>
        public string DebugRedirectAllTo { get; set; } = string.Empty;

        /// <summary>
        /// 送信操作1回につき1行を記録する履歴モジュール名 (役割は MailHistoryContractField:
        /// SentAt / MailInfraName / Subject / TotalCount / SuccessCount / FailureDetails / SourceModule / SourceId)。
        /// 空 = 履歴なし。検証は実行時。履歴の異常が送信自体を失敗させることはない。
        /// </summary>
        public string HistoryModuleName { get; set; } = string.Empty;

        public List<MailInfraSettings> Infras { get; set; } = new();

        /// <summary>
        /// 単発送信でインフラ名を省略したときの既定。空 = 先頭のインフラ。
        /// 通知系インフラ (Graph / SMTP) を指すのが典型。
        /// </summary>
        public string DefaultInfraName { get; set; } = string.Empty;

        /// <summary>
        /// 一斉送信でインフラ名を省略したときの既定。空 = <see cref="DefaultInfraName"/>
        /// (無ければ先頭)。配信サービス (SendGrid) を指すのが典型。
        /// </summary>
        public string DefaultBulkInfraName { get; set; } = string.Empty;

        /// <summary>
        /// 旧形式の単一 SMTP 設定 "MailSettings" を "Default" という名前のインフラとして統合する
        /// (既存アプリが設定変更なしで動き続けるための後方互換)。
        /// 同名のインフラが既にある場合は追加しない。
        /// </summary>
        public static MailConfig Normalize(MailConfig? config, MailSettings? legacySettings)
        {
            var result = config ?? new MailConfig();
            if (!string.IsNullOrEmpty(legacySettings?.Host) &&
                !result.Infras.Any(e => e.Name == MailInfraSettings.LegacyDefaultName))
            {
                result.Infras.Add(new MailInfraSettings
                {
                    Name = MailInfraSettings.LegacyDefaultName,
                    Type = MailInfraTypes.Smtp,
                    Host = legacySettings.Host,
                    Port = legacySettings.Port,
                    SSL = legacySettings.SSL,
                    Password = legacySettings.Password,
                    SenderMailAddress = legacySettings.SenderMailAddress,
                    SenderDisplayName = legacySettings.SenderDisplayName,
                });
            }
            return result;
        }
    }

    public static class MailInfraTypes
    {
        public const string Smtp = "Smtp";
        public const string GraphApi = "GraphApi";
        public const string SendGrid = "SendGrid";
        public const string GmailApi = "GmailApi";
    }

    /// <summary>名前付きメールインフラ1件。使われるプロパティは <see cref="Type"/> によって異なる。</summary>
    public class MailInfraSettings
    {
        internal const string LegacyDefaultName = "Default";

        public string Name { get; set; } = string.Empty;

        /// <summary>Smtp / GraphApi / SendGrid / GmailApi。<see cref="MailInfraTypes"/> 参照。</summary>
        public string Type { get; set; } = string.Empty;

        public string SenderMailAddress { get; set; } = string.Empty;
        public string SenderDisplayName { get; set; } = string.Empty;

        /// <summary>
        /// 動的な差出人 (MailMessage.From) を許可するドメイン。空 (既定) = 動的 From 不許可。
        /// SPF/DKIM/DMARC と送信インフラの SendAs 権限が整合するドメインだけを登録すること。
        /// </summary>
        public List<string> AllowedFromDomains { get; set; } = new();

        /// <summary>一斉送信1回の件数上限。超過はエラー (黙って切り詰めない)。</summary>
        public int MaxBulkCount { get; set; } = 10000;

        //Smtp
        public string Host { get; set; } = string.Empty;
        public string Port { get; set; } = string.Empty;
        public string SSL { get; set; } = string.Empty;
        /// <summary>SMTP 認証ユーザー。空なら <see cref="SenderMailAddress"/> を使う。</summary>
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        //GraphApi (client credentials)
        public string TenantId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// GraphApi: クライアントシークレット。
        /// GmailApi: サービスアカウントの JSON キー (ドメイン全体の委任モード)、または
        /// OAuth クライアントの client_secret JSON (installed/web = ユーザー同意モード)。どちらもファイルパスか JSON 文字列そのもの。
        /// </summary>
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// GmailApi のユーザー同意モード (ClientSecret が OAuth クライアントのとき) で使う、
        /// 同意で得たリフレッシュトークンの JSON ({"refresh_token":"..."}。ファイルパスか JSON 文字列そのもの)。
        /// 送信は同意したユーザー本人として行われる (管理者権限・ドメイン全体の委任は不要)。
        /// </summary>
        public string TokenSecret { get; set; } = string.Empty;

        //SendGrid
        public string ApiKey { get; set; } = string.Empty;
    }
}
