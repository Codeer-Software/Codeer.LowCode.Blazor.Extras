using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    /// <summary>One recipient of a bulk send built from scripts (BulkMail.AddRecipient).</summary>
    public class MailRecipient : MailBulkRecipient
    {
        [ScriptName("AddCc")]
        public MailRecipient AddCc(string address)
        {
            Cc.Add(address);
            return this;
        }

        [ScriptName("AddBcc")]
        public MailRecipient AddBcc(string address)
        {
            Bcc.Add(address);
            return this;
        }

        [ScriptName("SetVariable")]
        public MailRecipient SetVariable(string name, string? value)
        {
            Variables[name] = value ?? string.Empty;
            return this;
        }
    }
}
