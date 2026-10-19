using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Notifications
{
    /// <param name="BusinessId">Defaults to the business in scope. Give it explicitly for sign-in events, which happen before one is.</param>
    /// <param name="ActorUserId">Defaults to the signed-in user.</param>
    /// <param name="BranchId">The branch it is about. Branch-restricted recipients hear only about their own branches.</param>
    /// <param name="SubjectUserId">The user the event is about (the user created, the account locked, the cashier whose sale failed).</param>
    /// <param name="Data">Details for the audit log, serialised as JSON.</param>
    public sealed record SystemEventDraft(
        string EventType,
        string Summary,
        EventSeverity Severity = EventSeverity.Info,
        string? EntityType = null,
        Guid? EntityId = null,
        object? Data = null,
        Guid? BusinessId = null,
        Guid? ActorUserId = null,
        Guid? SubjectUserId = null,
        string? AlertKey = null,
        IEnumerable<string>? ClearsAlertKeys = null,
        Guid? BranchId = null);

    /// <summary>
    /// Records system events. <see cref="Record"/> adds the event to the current unit of work, so it commits with the change that
    /// caused it — call it BEFORE the repository saves. <see cref="RecordNowAsync"/> saves at once, for events with no change of
    /// their own to ride on (a sign-out, an action that has already been saved).
    /// </summary>
    public interface ISystemEventRecorder
    {
        void Record(SystemEventDraft draft);
        Task RecordNowAsync(SystemEventDraft draft);
        /// <summary>Saves events recorded with <see cref="Record"/> that no repository save has picked up yet.</summary>
        Task FlushAsync();
    }
}
