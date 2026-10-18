using System;

namespace UltimatePos.Domain.Entities
{
    /// <summary>One message to one user, shown in the in-app bell. Created by the dispatcher from a SystemEvent.</summary>
    public class Notification
    {
        public Guid NotificationId { get; set; }
        /// <summary>The business it concerns. Null for platform notifications, which show whichever business the user is in.</summary>
        public Guid? BusinessId { get; set; }
        public Guid UserId { get; set; }
        public Guid SystemEventId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public EventSeverity Severity { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        /// <summary>Frontend path to open, e.g. "/inventory/batches?productId=…".</summary>
        public string? Link { get; set; }
        public bool IsRead { get; set; }
        public DateTime? ReadAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum DeliveryChannel { Email, Sms }
    public enum DeliveryStatus { Pending, Sent, Failed, Skipped }

    /// <summary>
    /// A notification going out by email or SMS. Queued by the dispatcher and sent by the delivery worker, with retries, so a slow
    /// or failing mail server never holds up the request that caused the event.
    /// </summary>
    public class NotificationDelivery
    {
        public Guid NotificationDeliveryId { get; set; }
        public Guid? BusinessId { get; set; }
        public Guid? NotificationId { get; set; }
        public Guid SystemEventId { get; set; }
        public Guid? UserId { get; set; }
        public DeliveryChannel Channel { get; set; }
        /// <summary>Email address, or phone number in 2547XXXXXXXX form.</summary>
        public string Recipient { get; set; } = string.Empty;
        public string? Subject { get; set; }
        public string Body { get; set; } = string.Empty;
        public string? HtmlBody { get; set; }
        public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
        public int Attempts { get; set; }
        public DateTime? NextAttemptAt { get; set; }
        public string? LastError { get; set; }
        /// <summary>The provider's id for the message, for tracing with them.</summary>
        public string? ProviderMessageId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? SentAt { get; set; }
    }

    /// <summary>
    /// An open alert, so a condition is announced once — "Amoxicillin is low" when it drops, not again on every sale — and again
    /// only after it has cleared (restocked) and recurred.
    /// </summary>
    public class AlertState : ITenantEntity
    {
        public Guid AlertStateId { get; set; }
        public Guid BusinessId { get; set; }
        public string AlertKey { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public Guid? RaisedByEventId { get; set; }
        public DateTime RaisedAt { get; set; }
        public DateTime? ClearedAt { get; set; }
    }
}
