using System;

namespace UltimatePos.Domain.Entities
{
    public enum EventSeverity { Info, Warning, Critical }

    /// <summary>
    /// Something that happened in the system: a sale voided, a user created, stock falling below its reorder level. Written in the
    /// same transaction as the change it describes (an outbox), so an event exists exactly when the change does. Every event is
    /// the audit log; the notification dispatcher reads the unprocessed ones and turns those that matter into notifications.
    /// Append-only.
    /// </summary>
    public class SystemEvent
    {
        public Guid SystemEventId { get; set; }
        /// <summary>The business it happened in. Null for platform events (a business registered, a sign-in before one is chosen).</summary>
        public Guid? BusinessId { get; set; }
        /// <summary>One of SystemEventTypes, e.g. "Stock.BelowReorderLevel".</summary>
        public string EventType { get; set; } = string.Empty;
        public EventSeverity Severity { get; set; }
        /// <summary>One readable sentence, written when the event happened, with the names as they were then.</summary>
        public string Summary { get; set; } = string.Empty;
        /// <summary>Who did it. Null for the system itself (sweeps, webhooks).</summary>
        public Guid? ActorUserId { get; set; }
        /// <summary>The user the event is about (the user created, the account locked, the cashier whose payment failed).</summary>
        public Guid? SubjectUserId { get; set; }
        public string? EntityType { get; set; }
        public Guid? EntityId { get; set; }
        /// <summary>Event details as JSON, for the audit log and for notification templates.</summary>
        public string? Data { get; set; }
        /// <summary>A condition the event reports (e.g. "stock-low:{productId}"). While that alert is open, repeats notify nobody.</summary>
        public string? AlertKey { get; set; }
        /// <summary>Alert keys this event closes, comma-separated (e.g. a restock closes the low- and out-of-stock alerts).</summary>
        public string? ClearsAlertKeys { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

        // Outbox bookkeeping, owned by the dispatcher.
        public DateTime? ProcessedAt { get; set; }
        public int Attempts { get; set; }
        public string? LastError { get; set; }
    }
}
