namespace UltimatePos.Application.Notifications
{
    public sealed record EmailMessage(string To, string? ToName, string Subject, string TextBody, string? HtmlBody = null);

    /// <param name="PhoneNumber">International form without '+', e.g. 254712345678.</param>
    public sealed record SmsMessage(string PhoneNumber, string Text);

    /// <param name="Retryable">False when trying again cannot help (bad address, rejected number).</param>
    public sealed record ChannelSendResult(bool Success, string? ProviderMessageId = null, string? Error = null, bool Retryable = true)
    {
        public static ChannelSendResult Sent(string? providerMessageId = null) => new(true, providerMessageId);
        public static ChannelSendResult Failed(string error, bool retryable = true) => new(false, null, error, retryable);
    }

    /// <summary>
    /// Sends email. Implementations: SMTP (any mail server or relay — Gmail, Office 365, SendGrid/Mailgun SMTP) and a log-only sender
    /// for development. Implementations must not throw for a failed send; return a failed result so the delivery is retried.
    /// </summary>
    public interface IEmailSender
    {
        string ProviderName { get; }
        Task<ChannelSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Sends SMS. Only the log-only sender exists today. A provider such as Africa's Talking plugs in by implementing this and
    /// registering it for its name in Infrastructure's AddNotificationChannels — nothing else in the system changes.
    /// </summary>
    public interface ISmsSender
    {
        string ProviderName { get; }
        Task<ChannelSendResult> SendAsync(SmsMessage message, CancellationToken cancellationToken = default);
    }

    /// <summary>Configuration section "Notifications".</summary>
    public sealed class NotificationSettings
    {
        /// <summary>Queue email deliveries at all. Off = in-app only.</summary>
        public bool EmailEnabled { get; set; }
        /// <summary>Queue SMS deliveries at all.</summary>
        public bool SmsEnabled { get; set; }
        /// <summary>Base URL of the frontend, for links in emails (e.g. https://pos.example.co.ke).</summary>
        public string? FrontendBaseUrl { get; set; }
        /// <summary>Batches expiring within this many days raise a warning.</summary>
        public int ExpiryWarningDays { get; set; } = 30;
        /// <summary>A till payment still unmatched after this many minutes raises a warning.</summary>
        public int TillUnmatchedMinutes { get; set; } = 30;
        public int MaxDeliveryAttempts { get; set; } = 5;
        /// <summary>Run the background dispatcher and sweep in this process. Turn off on extra instances if you scale out.</summary>
        public bool WorkersEnabled { get; set; } = true;
        /// <summary>How often the dispatcher looks for new events and due deliveries.</summary>
        public int DispatchIntervalSeconds { get; set; } = 5;
        /// <summary>How often the alert sweep checks stock, expiry, orders and till payments.</summary>
        public int SweepIntervalMinutes { get; set; } = 60;
    }
}
