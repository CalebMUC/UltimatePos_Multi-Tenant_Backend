using System.Text.Json;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Notifications;
using UltimatePos.Domain.Entities;
using UltimatePos.Infrastructure.Persistence;

namespace UltimatePos.Infrastructure.Notifications
{
    /// <summary>
    /// Adds events to the request's DbContext, so they commit with whatever the request saves next — inside the same transaction
    /// when there is one. That is what makes the events table a reliable outbox: no change without its event, no event without its change.
    /// </summary>
    public class SystemEventRecorder : ISystemEventRecorder
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly UltimatePosDbContext _context;
        private readonly ITenantContext _tenant;
        private readonly ICurrentUser _currentUser;

        public SystemEventRecorder(UltimatePosDbContext context, ITenantContext tenant, ICurrentUser currentUser)
        {
            _context = context;
            _tenant = tenant;
            _currentUser = currentUser;
        }

        public void Record(SystemEventDraft draft) => _context.SystemEvents.Add(ToEntity(draft));

        public async Task RecordNowAsync(SystemEventDraft draft)
        {
            Record(draft);
            await _context.SaveChangesAsync();
        }

        public async Task FlushAsync()
        {
            if (_context.ChangeTracker.Entries<SystemEvent>().Any(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added))
                await _context.SaveChangesAsync();
        }

        private SystemEvent ToEntity(SystemEventDraft draft) => new()
        {
            SystemEventId = Guid.NewGuid(),
            BusinessId = draft.BusinessId ?? _tenant.BusinessId,
            BranchId = draft.BranchId,
            EventType = draft.EventType,
            Severity = draft.Severity,
            Summary = draft.Summary.Length <= 1000 ? draft.Summary : draft.Summary[..1000],
            ActorUserId = draft.ActorUserId ?? _currentUser.UserId,
            SubjectUserId = draft.SubjectUserId,
            EntityType = draft.EntityType,
            EntityId = draft.EntityId,
            Data = draft.Data is null ? null : JsonSerializer.Serialize(draft.Data, Json),
            AlertKey = draft.AlertKey,
            ClearsAlertKeys = draft.ClearsAlertKeys is null ? null : string.Join(',', draft.ClearsAlertKeys),
            OccurredAt = DateTime.UtcNow,
        };
    }
}
