using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Reports;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Application.Notifications
{
    public record NotificationDto(Guid NotificationId, string EventType, EventSeverity Severity, string Title, string Message, string? Link,
        bool IsRead, DateTime? ReadAt, DateTime CreatedAt, Guid? BusinessId);

    public record AuditLogEntryDto(Guid SystemEventId, string EventType, EventSeverity Severity, string Summary, Guid? ActorUserId,
        string? ActorName, Guid? SubjectUserId, string? SubjectName, string? EntityType, Guid? EntityId, string? Data, DateTime OccurredAt);

    /// <summary>The signed-in user's notifications (the bell), and the business's audit log.</summary>
    public class NotificationService
    {
        private readonly INotificationRepository _repository;
        private readonly ICurrentUser _currentUser;
        private readonly ITenantContext _tenant;

        public NotificationService(INotificationRepository repository, ICurrentUser currentUser, ITenantContext tenant)
        {
            _repository = repository;
            _currentUser = currentUser;
            _tenant = tenant;
        }

        private Guid UserId => _currentUser.UserId ?? throw new InvalidTokenException("Sign in first.");

        /// <summary>Newest first. Includes platform notifications, which show whichever business the user is signed in to.</summary>
        public async Task<PagedResult<NotificationDto>> GetNotificationsAsync(bool unreadOnly, int page, int pageSize)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var (items, total) = await _repository.GetNotificationsAsync(UserId, unreadOnly, page, pageSize);
            return new PagedResult<NotificationDto>(items.Select(ToDto), page, pageSize, total);
        }

        public Task<int> GetUnreadCountAsync() => _repository.GetUnreadCountAsync(UserId);

        public async Task MarkReadAsync(Guid notificationId)
        {
            if (!await _repository.MarkReadAsync(UserId, notificationId))
                throw new NotFoundException($"Notification '{notificationId}' not found.");
        }

        public Task<int> MarkAllReadAsync() => _repository.MarkAllReadAsync(UserId);

        /// <summary>Everything recorded in the business signed in to, newest first. from/to are local days, both inclusive.</summary>
        public async Task<PagedResult<AuditLogEntryDto>> GetAuditLogAsync(int page, int pageSize, DateOnly? from, DateOnly? to, int utcOffsetMinutes,
            string? eventType, Guid? actorUserId, string? entityType, Guid? entityId, EventSeverity? minimumSeverity, string? search)
        {
            _tenant.RequireBusinessId();
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);
            ReportPeriod.ValidateOffset(utcOffsetMinutes);
            if (from.HasValue && to.HasValue && from > to)
                throw new ValidationException("from", "'from' cannot be after 'to'.");
            if (search is { Length: > 100 })
                throw new ValidationException("search", "Search text cannot exceed 100 characters.");

            var offset = TimeSpan.FromMinutes(utcOffsetMinutes);
            var query = new AuditLogQuery(
                from.HasValue ? ReportPeriod.StartOfLocalDayUtc(from.Value, offset) : null,
                to.HasValue ? ReportPeriod.StartOfLocalDayUtc(to.Value.AddDays(1), offset) : null,
                string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim(), actorUserId,
                string.IsNullOrWhiteSpace(entityType) ? null : entityType.Trim(), entityId, minimumSeverity,
                string.IsNullOrWhiteSpace(search) ? null : search.Trim());

            var (items, total) = await _repository.GetEventsAsync(query, page, pageSize);
            var names = await _repository.GetUserNamesAsync(items.SelectMany(e => new[] { e.ActorUserId, e.SubjectUserId }).OfType<Guid>());

            return new PagedResult<AuditLogEntryDto>(items.Select(e => new AuditLogEntryDto(e.SystemEventId, e.EventType, e.Severity, e.Summary,
                e.ActorUserId, e.ActorUserId is { } a && names.TryGetValue(a, out var actor) ? actor : null,
                e.SubjectUserId, e.SubjectUserId is { } s && names.TryGetValue(s, out var subject) ? subject : null,
                e.EntityType, e.EntityId, e.Data, e.OccurredAt)), page, pageSize, total);
        }

        private static NotificationDto ToDto(Notification n) =>
            new(n.NotificationId, n.EventType, n.Severity, n.Title, n.Message, n.Link, n.IsRead, n.ReadAt, n.CreatedAt, n.BusinessId);
    }
}
