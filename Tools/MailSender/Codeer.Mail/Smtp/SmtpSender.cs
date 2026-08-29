using MailKit.Net.Smtp;
using MailKit.Security;

namespace Codeer.Mail.Smtp
{
    /// <summary>SMTP の暗号化方式。</summary>
    public enum SmtpEncryption
    {
        /// <summary>STARTTLS (通常 587)。</summary>
        StartTls,
        /// <summary>接続時から SSL/TLS (通常 465)。</summary>
        SslOnConnect,
        /// <summary>暗号化なし (社内サーバー・検証用)。</summary>
        None,
    }

    /// <summary>SMTP サーバーと差出人の設定 (1 アカウント分)。</summary>
    public class SmtpAccountSettings
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public SmtpEncryption Encryption { get; set; } = SmtpEncryption.StartTls;

        /// <summary>認証ユーザー。空なら <see cref="Email"/>。認証なしのサーバーはパスワードも空にする。</summary>
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        /// <summary>差出人アドレス (From)。</summary>
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        public bool UseAuthentication => !string.IsNullOrEmpty(Password);
        public string EffectiveUserName => string.IsNullOrEmpty(UserName) ? Email : UserName;

        public SecureSocketOptions SocketOptions => Encryption switch
        {
            SmtpEncryption.SslOnConnect => SecureSocketOptions.SslOnConnect,
            SmtpEncryption.None => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTls,
        };
    }

    /// <summary>
    /// SMTP (MailKit) で送る。1 接続を開いたまま複数通を逐次送る (<see cref="ConnectAsync"/> → <see cref="SendAsync"/>×n → Dispose)。
    /// 接続・認証の失敗は <see cref="MailSendAbortException"/> (残りを送っても同じ結果)。
    /// </summary>
    public sealed class SmtpSender : IAsyncDisposable
    {
        readonly SmtpAccountSettings _settings;
        readonly SmtpClient _client = new();

        public SmtpSender(SmtpAccountSettings settings)
        {
            _settings = settings;
        }

        /// <summary>接続して認証する。接続テストにもそのまま使う。</summary>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _client.ConnectAsync(_settings.Host, _settings.Port, _settings.SocketOptions, cancellationToken);
                if (_settings.UseAuthentication)
                    await _client.AuthenticateAsync(_settings.EffectiveUserName, _settings.Password, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new MailSendAbortException($"SMTP サーバーに接続できませんでした ({_settings.Host}:{_settings.Port}): {ex.Message}", ex);
            }
        }

        /// <summary>1 通送る。From が空なら設定の差出人。</summary>
        public async Task SendAsync(MailMessage message, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(message.From))
            {
                message.From = _settings.Email;
                message.FromDisplayName = _settings.DisplayName;
            }
            var mime = MimeBuilder.CreateMimeMessage(message);
            try
            {
                await _client.SendAsync(mime, cancellationToken);
            }
            catch (SmtpCommandException ex) when (ex.ErrorCode is SmtpErrorCode.RecipientNotAccepted or SmtpErrorCode.MessageNotAccepted)
            {
                //宛先固有の失敗 → この 1 通だけ失敗
                throw new InvalidOperationException(ex.Message, ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !_client.IsConnected)
            {
                throw new MailSendAbortException($"SMTP サーバーとの接続が切れました: {ex.Message}", ex);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_client.IsConnected) await _client.DisconnectAsync(true);
            }
            catch { }
            _client.Dispose();
        }
    }
}
