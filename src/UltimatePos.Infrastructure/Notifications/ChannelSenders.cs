using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using UltimatePos.Application.Notifications;

namespace UltimatePos.Infrastructure.Notifications
{
    /// <summary>Configuration section "Email".</summary>
    public sealed class EmailOptions
    {
        /// <summary>"Log" (development: writes the email to the log) or "Smtp".</summary>
        public string Provider { get; set; } = "Log";
        public string FromAddress { get; set; } = "no-reply@ultimatepos.local";
        public string FromName { get; set; } = "Ultimate POS";
        public SmtpOptions Smtp { get; set; } = new();
    }

    public sealed class SmtpOptions
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        /// <summary>"StartTls" (port 587, the usual), "SslOnConnect" (port 465) or "None" (local relays only).</summary>
        public string Security { get; set; } = "StartTls";
        public string? Username { get; set; }
        /// <summary>Keep out of appsettings: use user-secrets locally and an environment variable or vault in production.</summary>
        public string? Password { get; set; }
        public int TimeoutSeconds { get; set; } = 30;
    }

    /// <summary>Configuration section "Sms".</summary>
    public sealed class SmsOptions
    {
        /// <summary>"Log" is the only provider today. "AfricasTalking" is reserved for the integration below.</summary>
        public string Provider { get; set; } = "Log";
        /// <summary>The sender name/short code registered with the provider.</summary>
        public string? SenderId { get; set; }
        public AfricasTalkingOptions AfricasTalking { get; set; } = new();
    }

    /// <summary>
    /// Settings for the Africa's Talking SMS integration, fixed now so configuration does not change when it lands. To add it:
    /// implement ISmsSender as AfricasTalkingSmsSender (POST {BaseUrl}/version1/messaging with username, to, message, from; header
    /// apiKey), and map "AfricasTalking" to it in NotificationChannelRegistration. Use the sandbox (username "sandbox",
    /// https://api.sandbox.africastalking.com) until it is verified.
    /// </summary>
    public sealed class AfricasTalkingOptions
    {
        public string Username { get; set; } = "sandbox";
        public string? ApiKey { get; set; }
        public string BaseUrl { get; set; } = "https://api.sandbox.africastalking.com";
    }

    /// <summary>Sends through any SMTP server or relay (Office 365, Gmail/Workspace, SendGrid, Mailgun, Amazon SES SMTP...).</summary>
    public sealed class SmtpEmailSender : IEmailSender
    {
        private readonly EmailOptions _options;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public string ProviderName => "Smtp";

        public async Task<ChannelSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            MimeMessage mime;
            try
            {
                mime = new MimeMessage();
                mime.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
                mime.To.Add(new MailboxAddress(message.ToName ?? string.Empty, message.To));
                mime.Subject = message.Subject;
                mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();
            }
            catch (ParseException ex)
            {
                return ChannelSendResult.Failed($"Invalid address: {ex.Message}", retryable: false);
            }

            var smtp = _options.Smtp;
            using var client = new SmtpClient { Timeout = smtp.TimeoutSeconds * 1000 };
            try
            {
                var security = smtp.Security switch
                {
                    "SslOnConnect" => SecureSocketOptions.SslOnConnect,
                    "None" => SecureSocketOptions.None,
                    _ => SecureSocketOptions.StartTls,
                };
                await client.ConnectAsync(smtp.Host, smtp.Port, security, cancellationToken);
                if (!string.IsNullOrEmpty(smtp.Username))
                    await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty, cancellationToken);
                var response = await client.SendAsync(mime, cancellationToken);
                await client.DisconnectAsync(true, cancellationToken);
                return ChannelSendResult.Sent(response);
            }
            catch (SmtpCommandException ex) when (ex.ErrorCode is SmtpErrorCode.RecipientNotAccepted)
            {
                return ChannelSendResult.Failed($"Recipient rejected: {ex.Message}", retryable: false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Email to {To} failed", message.To);
                return ChannelSendResult.Failed(ex.Message);
            }
        }
    }

    /// <summary>Development: the email is written to the log instead of being sent, and counts as delivered.</summary>
    public sealed class LogEmailSender : IEmailSender
    {
        private readonly ILogger<LogEmailSender> _logger;
        public LogEmailSender(ILogger<LogEmailSender> logger) => _logger = logger;
        public string ProviderName => "Log";

        public Task<ChannelSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("EMAIL (not sent — Email:Provider is Log) to {To}: {Subject}\n{Body}", message.To, message.Subject, message.TextBody);
            return Task.FromResult(ChannelSendResult.Sent("log"));
        }
    }

    /// <summary>The SMS channel until a provider is integrated: the message is written to the log and counts as delivered.</summary>
    public sealed class LogSmsSender : ISmsSender
    {
        private readonly ILogger<LogSmsSender> _logger;
        public LogSmsSender(ILogger<LogSmsSender> logger) => _logger = logger;
        public string ProviderName => "Log";

        public Task<ChannelSendResult> SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("SMS (not sent — Sms:Provider is Log) to {Phone}: {Text}", message.PhoneNumber, message.Text);
            return Task.FromResult(ChannelSendResult.Sent("log"));
        }
    }
}
