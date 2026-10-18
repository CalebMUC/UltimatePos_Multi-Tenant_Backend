using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Notifications;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Api.Controllers
{
    /// <summary>The signed-in user's own notifications — the bell. Needs only a sign-in: everyone sees what was sent to them.</summary>
    [ApiController]
    [Route("api/v1/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly NotificationService _notifications;
        public NotificationsController(NotificationService notifications) => _notifications = notifications;

        [HttpGet]
        public async Task<IActionResult> GetNotifications([FromQuery] bool unreadOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var paged = await _notifications.GetNotificationsAsync(unreadOnly, page, pageSize);
            var meta = new PagingMeta(paged.Page, paged.PageSize, paged.TotalCount, paged.TotalPages);
            return Ok(ApiResponse<IEnumerable<NotificationDto>>.Ok(paged.Items, meta));
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var count = await _notifications.GetUnreadCountAsync();
            return Ok(ApiResponse<object>.Ok(new { unreadCount = count }));
        }

        [HttpPost("{id:guid}/read")]
        public async Task<IActionResult> MarkRead(Guid id)
        {
            await _notifications.MarkReadAsync(id);
            return Ok(ApiResponse<object>.Ok(new { }));
        }

        [HttpPost("read-all")]
        public async Task<IActionResult> MarkAllRead()
        {
            var marked = await _notifications.MarkAllReadAsync();
            return Ok(ApiResponse<object>.Ok(new { marked }));
        }
    }

    /// <summary>Everything recorded in the business signed in to: sign-ins, changes, alerts.</summary>
    [ApiController]
    [Route("api/v1/audit-log")]
    public class AuditLogController : ControllerBase
    {
        private readonly NotificationService _notifications;
        public AuditLogController(NotificationService notifications) => _notifications = notifications;

        // from / to are local calendar days, both inclusive. eventType is an exact type ("Sale.Voided") or an area ("Sale").
        [Authorize(Policy = "PERMISSION:AuditLog.View")]
        [HttpGet]
        public async Task<IActionResult> GetAuditLog(
            [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
            [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] int utcOffsetMinutes = 180,
            [FromQuery] string? eventType = null, [FromQuery] Guid? actorUserId = null,
            [FromQuery] string? entityType = null, [FromQuery] Guid? entityId = null,
            [FromQuery] EventSeverity? minimumSeverity = null, [FromQuery] string? search = null)
        {
            var paged = await _notifications.GetAuditLogAsync(page, pageSize, from, to, utcOffsetMinutes, eventType, actorUserId,
                entityType, entityId, minimumSeverity, search);
            var meta = new PagingMeta(paged.Page, paged.PageSize, paged.TotalCount, paged.TotalPages);
            return Ok(ApiResponse<IEnumerable<AuditLogEntryDto>>.Ok(paged.Items, meta));
        }

        /// <summary>The event types that can appear, for the filter dropdown.</summary>
        [Authorize(Policy = "PERMISSION:AuditLog.View")]
        [HttpGet("event-types")]
        public IActionResult GetEventTypes() =>
            Ok(ApiResponse<IEnumerable<string>>.Ok(typeof(SystemEventTypes).GetFields()
                .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).OrderBy(t => t)));
    }
}
