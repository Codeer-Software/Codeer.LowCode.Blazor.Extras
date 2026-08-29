using MimeKit;

namespace Codeer.Mail
{
    /// <summary>添付 1 件 (内容は Base64)。</summary>
    public class MailAttachment
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentBase64 { get; set; } = string.Empty;
    }

    /// <summary>
    /// 送る 1 通 (プロバイダ共通)。<see cref="From"/> が空なら差出人はアカウント側で決まる
    /// (Gmail / Graph = トークンのアカウント本人。SMTP はアカウントに登録したアドレス)。
    /// </summary>
    public class MailMessage
    {
        public string From { get; set; } = string.Empty;
        public string FromDisplayName { get; set; } = string.Empty;
        public List<string> To { get; set; } = new();
        public List<string> Cc { get; set; } = new();
        public List<string> Bcc { get; set; } = new();
        public string ReplyTo { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public bool IsBodyHtml { get; set; }
        public List<MailAttachment> Attachments { get; set; } = new();

        /// <summary>追加ヘッダ ("X-" で始めること)。</summary>
        public Dictionary<string, string> Headers { get; set; } = new();
    }

    /// <summary>MIME (RFC 2822) の組み立て。Gmail API は base64url にした "raw" で、SMTP はそのまま送る。</summary>
    public static class MimeBuilder
    {
        public static MimeMessage CreateMimeMessage(MailMessage message)
        {
            var mime = new MimeMessage();
            if (!string.IsNullOrEmpty(message.From))
                mime.From.Add(new MailboxAddress(message.FromDisplayName, message.From));
            foreach (var e in message.To) mime.To.Add(MailboxAddress.Parse(e));
            foreach (var e in message.Cc) mime.Cc.Add(MailboxAddress.Parse(e));
            foreach (var e in message.Bcc) mime.Bcc.Add(MailboxAddress.Parse(e));
            if (!string.IsNullOrEmpty(message.ReplyTo)) mime.ReplyTo.Add(MailboxAddress.Parse(message.ReplyTo));
            mime.Subject = message.Subject;
            foreach (var e in message.Headers) mime.Headers.Add(e.Key, e.Value);

            var builder = new BodyBuilder();
            if (message.IsBodyHtml) builder.HtmlBody = message.Body;
            else builder.TextBody = message.Body;
            foreach (var e in message.Attachments)
                builder.Attachments.Add(e.FileName, Convert.FromBase64String(e.ContentBase64));
            mime.Body = builder.ToMessageBody();
            return mime;
        }

        /// <summary>MIME 全体のバイト列。</summary>
        public static async Task<byte[]> CreateRawAsync(MailMessage message)
        {
            using var stream = new MemoryStream();
            await CreateMimeMessage(message).WriteToAsync(stream);
            return stream.ToArray();
        }
    }

    /// <summary>
    /// 残りを送っても無駄な失敗 (1 日の送信上限、接続不能、認証失敗など)。送信ループはこれで残りを打ち切る。
    /// </summary>
    public class MailSendAbortException : InvalidOperationException
    {
        public MailSendAbortException(string message) : base(message) { }
        public MailSendAbortException(string message, Exception inner) : base(message, inner) { }
    }
}
