using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// Mail sending infrastructure. Implementations exist per infrastructure
    /// (SMTP / Microsoft Graph / SendGrid) and custom ones can be plugged in
    /// via <see cref="MailDispatcher"/>.
    /// </summary>
    public interface IMailSender
    {
        /// <summary>Sends a single message.</summary>
        Task<MailSendResult> SendAsync(MailMessage message);

        /// <summary>
        /// Sends one template to many recipients with per-recipient variables.
        /// Implementations with native bulk APIs (SendGrid personalizations) map to them;
        /// others resolve the template per recipient and send sequentially.
        /// Partial failures are reported per recipient - the call itself does not throw for them.
        /// </summary>
        Task<MailSendResult> SendBulkAsync(MailBulkTemplate template, List<MailBulkRecipient> recipients);
    }
}
