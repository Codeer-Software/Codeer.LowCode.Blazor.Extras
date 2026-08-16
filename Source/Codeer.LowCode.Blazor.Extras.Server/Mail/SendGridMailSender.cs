using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// <see cref="IMailSender"/> の SendGrid (v3 mail/send) 実装。素の REST (SDK なし)。
    /// 一斉送信はネイティブの personalizations (1リクエスト最大1000宛先) に対応させているため、
    /// 大量送信にはこのインフラを推奨。
    /// </summary>
    public class SendGridMailSender : IMailSender
    {
        static readonly HttpClient _sharedClient = new();
        internal const int PersonalizationsPerRequest = 1000;
        const string Endpoint = "https://api.sendgrid.com/v3/mail/send";

        readonly MailInfraSettings _settings;
        readonly HttpClient _http;

        public SendGridMailSender(MailInfraSettings settings, HttpClient? httpClient = null)
        {
            _settings = settings;
            _http = httpClient ?? _sharedClient;
        }

        public async Task<MailSendResult> SendAsync(MailMessage message)
        {
            var personalization = new JsonObject { ["to"] = Addresses(message.To) };
            if (message.Cc.Any()) personalization["cc"] = Addresses(message.Cc);
            if (message.Bcc.Any()) personalization["bcc"] = Addresses(message.Bcc);

            var payload = CreatePayloadBase(message.Subject, message.Body, message.IsBodyHtml, message.ReplyTo, message.Attachments,
                message.From, message.FromDisplayName);
            payload["personalizations"] = new JsonArray(personalization);
            if (message.Headers.Any())
            {
                var headers = new JsonObject();
                foreach (var e in message.Headers) headers[e.Key] = e.Value;
                payload["headers"] = headers;
            }

            try
            {
                await PostAsync(payload);
                return MailSendResult.Success(1);
            }
            catch (Exception ex)
            {
                return MailSendResult.Failure(string.Join(";", message.To), ex.Message);
            }
        }

        public async Task<MailSendResult> SendBulkAsync(MailBulkTemplate template, List<MailBulkRecipient> recipients)
        {
            var result = new MailSendResult { TotalCount = recipients.Count };
            foreach (var chunk in recipients.Chunk(PersonalizationsPerRequest))
            {
                var payload = CreatePayloadBase(template.Subject, template.Body, template.IsBodyHtml, template.ReplyTo, template.Attachments,
                    template.From, template.FromDisplayName);
                payload["personalizations"] = new JsonArray(chunk.Select(r =>
                {
                    var personalization = new JsonObject { ["to"] = Addresses(new[] { r.To }) };
                    if (r.Cc.Any()) personalization["cc"] = Addresses(r.Cc);
                    if (r.Bcc.Any()) personalization["bcc"] = Addresses(r.Bcc);
                    if (r.Variables.Any())
                    {
                        //substitutions により件名・本文の {変数} は SendGrid 側で差し込まれる
                        var substitutions = new JsonObject();
                        foreach (var v in r.Variables) substitutions["{" + v.Key + "}"] = v.Value;
                        personalization["substitutions"] = substitutions;
                    }
                    return (JsonNode)personalization;
                }).ToArray());

                try
                {
                    await PostAsync(payload);
                    result.SuccessCount += chunk.Length;
                }
                catch (Exception ex)
                {
                    result.Failures.AddRange(chunk.Select(e => new MailSendFailure { To = e.To, Error = ex.Message }));
                }
            }
            return result;
        }

        JsonObject CreatePayloadBase(string subject, string body, bool isBodyHtml, string replyTo, List<MailAttachment> attachments,
            string fromOverride, string fromDisplayNameOverride)
        {
            //動的 From (許可ドメインは MailDispatcher が検証済み。ドメイン認証済みなら送信可)。空なら送信者設定の差出人
            var fromAddress = string.IsNullOrEmpty(fromOverride) ? _settings.SenderMailAddress : fromOverride;
            var fromName = string.IsNullOrEmpty(fromOverride) ? _settings.SenderDisplayName : fromDisplayNameOverride;
            var from = new JsonObject { ["email"] = fromAddress };
            if (!string.IsNullOrEmpty(fromName)) from["name"] = fromName;

            var payload = new JsonObject
            {
                ["from"] = from,
                ["subject"] = subject,
                //SendGrid は空の content を拒否する
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = isBodyHtml ? "text/html" : "text/plain",
                    ["value"] = string.IsNullOrEmpty(body) ? " " : body,
                }),
            };
            if (!string.IsNullOrEmpty(replyTo)) payload["reply_to"] = new JsonObject { ["email"] = replyTo };
            if (attachments.Any())
            {
                payload["attachments"] = new JsonArray(attachments.Select(e => (JsonNode)new JsonObject
                {
                    ["content"] = e.ContentBase64,
                    ["filename"] = e.FileName,
                    ["disposition"] = "attachment",
                }).ToArray());
            }
            return payload;
        }

        static JsonArray Addresses(IEnumerable<string> addresses)
            => new(addresses.Select(e => (JsonNode)new JsonObject { ["email"] = e }).ToArray());

        async Task PostAsync(JsonObject payload)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);
            request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Accepted || response.IsSuccessStatusCode) return;
            throw new InvalidOperationException($"SendGrid mail/send failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        }
    }
}
