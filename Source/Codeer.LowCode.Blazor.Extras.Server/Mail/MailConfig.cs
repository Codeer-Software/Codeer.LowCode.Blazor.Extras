namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// "Mail" section of appsettings.json. Named senders with different infrastructures
    /// (SMTP / Microsoft Graph / SendGrid) plus app-wide options.
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
        /// Module that records one row per send operation (reserved field names: SentAt / SenderName /
        /// Subject / TotalCount / SuccessCount / FailureDetails / SourceModule / SourceId).
        /// Empty = no history. Validated at runtime; a broken history never fails the send itself.
        /// </summary>
        public string HistoryModuleName { get; set; } = string.Empty;

        public List<MailSenderSettings> Senders { get; set; } = new();

        /// <summary>
        /// Merges the legacy single-SMTP "MailSettings" section as a sender named "Default"
        /// so that existing apps keep working without config changes.
        /// The legacy sender is appended only when no sender with the same name exists.
        /// </summary>
        public static MailConfig Normalize(MailConfig? config, MailSettings? legacySettings)
        {
            var result = config ?? new MailConfig();
            if (!string.IsNullOrEmpty(legacySettings?.Host) &&
                !result.Senders.Any(e => e.Name == MailSenderSettings.LegacyDefaultName))
            {
                result.Senders.Add(new MailSenderSettings
                {
                    Name = MailSenderSettings.LegacyDefaultName,
                    Type = MailSenderTypes.Smtp,
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

    public static class MailSenderTypes
    {
        public const string Smtp = "Smtp";
        public const string GraphApi = "GraphApi";
        public const string SendGrid = "SendGrid";
    }

    /// <summary>One named sender. The set of used properties depends on <see cref="Type"/>.</summary>
    public class MailSenderSettings
    {
        internal const string LegacyDefaultName = "Default";

        public string Name { get; set; } = string.Empty;

        /// <summary>Smtp / GraphApi / SendGrid. See <see cref="MailSenderTypes"/>.</summary>
        public string Type { get; set; } = string.Empty;

        public string SenderMailAddress { get; set; } = string.Empty;
        public string SenderDisplayName { get; set; } = string.Empty;

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
        public string ClientSecret { get; set; } = string.Empty;

        //SendGrid
        public string ApiKey { get; set; } = string.Empty;
    }
}
