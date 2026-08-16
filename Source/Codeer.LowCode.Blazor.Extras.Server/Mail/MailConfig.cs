namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// "Mail" section of appsettings.json. Named senders with different infrastructures
    /// (SMTP / Microsoft Graph / SendGrid / Gmail) plus app-wide options.
    /// </summary>
    public class MailConfig
    {
        /// <summary>Base URL of the app. Used to build record deep links in mail bodies.</summary>
        public string AppBaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// When set, every mail is redirected to this address instead of the real recipients
        /// (safety net for development/staging). Original recipients are recorded in the
        /// X-CLB-Original-To header. Bulk sends are clipped to the first 10 mails.
        /// </summary>
        public string RedirectAllTo { get; set; } = string.Empty;

        /// <summary>
        /// Module that records one row per send operation (reserved field names: SentAt / MailInfraName /
        /// Subject / TotalCount / SuccessCount / FailureDetails / SourceModule / SourceId).
        /// Empty = no history. Validated at runtime; a broken history never fails the send itself.
        /// </summary>
        public string HistoryModuleName { get; set; } = string.Empty;

        public List<MailInfraSettings> Infras { get; set; } = new();

        /// <summary>
        /// Sender used when a single send does not specify one. Empty = the first sender.
        /// Typically the notification infrastructure (Graph / SMTP).
        /// </summary>
        public string DefaultInfraName { get; set; } = string.Empty;

        /// <summary>
        /// Sender used when a bulk send does not specify one. Empty = <see cref="DefaultInfraName"/>
        /// (then the first sender). Typically a delivery service (SendGrid).
        /// </summary>
        public string DefaultBulkInfraName { get; set; } = string.Empty;

        /// <summary>
        /// Merges the legacy single-SMTP "MailSettings" section as a sender named "Default"
        /// so that existing apps keep working without config changes.
        /// The legacy sender is appended only when no sender with the same name exists.
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

    /// <summary>One named sender. The set of used properties depends on <see cref="Type"/>.</summary>
    public class MailInfraSettings
    {
        internal const string LegacyDefaultName = "Default";

        public string Name { get; set; } = string.Empty;

        /// <summary>Smtp / GraphApi / SendGrid / GmailApi. See <see cref="MailInfraTypes"/>.</summary>
        public string Type { get; set; } = string.Empty;

        public string SenderMailAddress { get; set; } = string.Empty;
        public string SenderDisplayName { get; set; } = string.Empty;

        /// <summary>
        /// 動的な差出人 (MailMessage.From) を許可するドメイン。空 (既定) = 動的 From 不許可。
        /// SPF/DKIM/DMARC と送信インフラの SendAs 権限が整合するドメインだけを登録すること。
        /// </summary>
        public List<string> AllowedFromDomains { get; set; } = new();

        /// <summary>Upper limit for one bulk send. Exceeding it is an error (never silently truncated).</summary>
        public int MaxBulkCount { get; set; } = 10000;

        //Smtp
        public string Host { get; set; } = string.Empty;
        public string Port { get; set; } = string.Empty;
        public string SSL { get; set; } = string.Empty;
        /// <summary>SMTP auth user. When empty, <see cref="SenderMailAddress"/> is used.</summary>
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        //GraphApi (client credentials)
        public string TenantId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;

        /// <summary>GraphApi: the client secret. GmailApi: the service account JSON key (file path, or the JSON text itself).</summary>
        public string ClientSecret { get; set; } = string.Empty;

        //SendGrid
        public string ApiKey { get; set; } = string.Empty;
    }
}
