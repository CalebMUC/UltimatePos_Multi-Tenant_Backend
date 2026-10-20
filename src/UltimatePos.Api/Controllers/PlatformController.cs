using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Identity.Dtos;
using UltimatePos.Application.Notifications;
using UltimatePos.Application.Platform;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Api.Controllers
{
    /// <summary>
    /// The super-user area. Every action needs a platform permission (held only through the Super User role, which belongs to no
    /// business) and works across businesses. Business registration is POST /api/v1/businesses.
    /// </summary>
    [ApiController]
    [Route("api/v1/platform")]
    public class PlatformController : ControllerBase
    {
        private readonly PlatformService _platform;
        public PlatformController(PlatformService platform) => _platform = platform;

        // ---- Performance ----

        /// <summary>How every business is doing: sales, revenue, users, last activity, licence position. Defaults to the last 30 days.</summary>
        [Authorize(Policy = "PERMISSION:Platform.Dashboard.View")]
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard([FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] int utcOffsetMinutes = 180)
        {
            var result = await _platform.GetDashboardAsync(from, to, utcOffsetMinutes);
            return Ok(ApiResponse<PlatformDashboardDto>.Ok(result));
        }

        // ---- Businesses ----

        /// <summary>Every business; sector narrows to one line of business (Pharmacy, WaterRefill, ...).</summary>
        [Authorize(Policy = "PERMISSION:Businesses.ViewAll")]
        [HttpGet("businesses")]
        public async Task<IActionResult> GetBusinesses([FromQuery] BusinessSector? sector = null)
        {
            var result = await _platform.GetBusinessesAsync(sector);
            return Ok(ApiResponse<IEnumerable<BusinessSummaryDto>>.Ok(result));
        }

        /// <summary>Profile, access status, licence history, members and the last 30 days' activity.</summary>
        [Authorize(Policy = "PERMISSION:Businesses.ViewAll")]
        [HttpGet("businesses/{id:guid}")]
        public async Task<IActionResult> GetBusiness(Guid id)
        {
            var result = await _platform.GetBusinessAsync(id);
            return Ok(ApiResponse<BusinessDetailDto>.Ok(result));
        }

        /// <summary>Locks a business (e.g. unpaid licence): its users are refused until it is reactivated. Data is kept.</summary>
        [Authorize(Policy = "PERMISSION:Businesses.Suspend")]
        [HttpPost("businesses/{id:guid}/suspend")]
        public async Task<IActionResult> Suspend(Guid id, ReasonRequestDto request) =>
            Ok(ApiResponse<BusinessSummaryDto>.Ok(await _platform.SuspendAsync(id, request)));

        [Authorize(Policy = "PERMISSION:Businesses.Suspend")]
        [HttpPost("businesses/{id:guid}/reactivate")]
        public async Task<IActionResult> Reactivate(Guid id) =>
            Ok(ApiResponse<BusinessSummaryDto>.Ok(await _platform.ReactivateAsync(id)));

        /// <summary>Reclassifies a business. Only the classification changes; what it started with stays.</summary>
        [Authorize(Policy = "PERMISSION:Businesses.Register")]
        [HttpPut("businesses/{id:guid}/sector")]
        public async Task<IActionResult> ChangeSector(Guid id, ChangeSectorRequestDto request) =>
            Ok(ApiResponse<BusinessSummaryDto>.Ok(await _platform.ChangeSectorAsync(id, request)));

        /// <summary>Closes a business that has left the platform. Reversible with reopen; data is kept.</summary>
        [Authorize(Policy = "PERMISSION:Businesses.Deactivate")]
        [HttpPost("businesses/{id:guid}/close")]
        public async Task<IActionResult> Close(Guid id, ReasonRequestDto request) =>
            Ok(ApiResponse<BusinessSummaryDto>.Ok(await _platform.CloseAsync(id, request?.Reason)));

        [Authorize(Policy = "PERMISSION:Businesses.Deactivate")]
        [HttpPost("businesses/{id:guid}/reopen")]
        public async Task<IActionResult> Reopen(Guid id) =>
            Ok(ApiResponse<BusinessSummaryDto>.Ok(await _platform.ReopenAsync(id)));

        // ---- Licences ----

        [Authorize(Policy = "PERMISSION:Licenses.View")]
        [HttpGet("businesses/{id:guid}/licenses")]
        public async Task<IActionResult> GetLicenses(Guid id) =>
            Ok(ApiResponse<IEnumerable<BusinessLicenseDto>>.Ok(await _platform.GetLicensesAsync(id)));

        /// <summary>Issues a licence period. With no start date it continues from the current one (a renewal), or starts today.</summary>
        [Authorize(Policy = "PERMISSION:Licenses.Manage")]
        [HttpPost("businesses/{id:guid}/licenses")]
        public async Task<IActionResult> IssueLicense(Guid id, IssueLicenseRequestDto request) =>
            Ok(ApiResponse<BusinessLicenseDto>.Ok(await _platform.IssueLicenseAsync(id, request)));

        [Authorize(Policy = "PERMISSION:Licenses.Manage")]
        [HttpPost("licenses/{licenseId:guid}/cancel")]
        public async Task<IActionResult> CancelLicense(Guid licenseId, ReasonRequestDto request) =>
            Ok(ApiResponse<BusinessLicenseDto>.Ok(await _platform.CancelLicenseAsync(licenseId, request)));

        /// <summary>Licences ending in the next `withinDays` (default 30), and those already ended but still in their grace period.</summary>
        [Authorize(Policy = "PERMISSION:Licenses.View")]
        [HttpGet("licenses/expiring")]
        public async Task<IActionResult> GetExpiringLicenses([FromQuery] int withinDays = 30) =>
            Ok(ApiResponse<IEnumerable<BusinessLicenseDto>>.Ok(await _platform.GetExpiringLicensesAsync(withinDays)));

        // ---- Support ----

        [Authorize(Policy = "PERMISSION:Platform.Support")]
        [HttpGet("businesses/{id:guid}/users")]
        public async Task<IActionResult> GetBusinessUsers(Guid id) =>
            Ok(ApiResponse<IEnumerable<UserDto>>.Ok(await _platform.GetBusinessUsersAsync(id)));

        /// <summary>Adds an administrator to a business: an existing account by email, or a new one (temporary password returned once).</summary>
        [Authorize(Policy = "PERMISSION:Platform.Support")]
        [HttpPost("businesses/{id:guid}/admins")]
        public async Task<IActionResult> AddBusinessAdmin(Guid id, BusinessOwnerRequestDto request) =>
            Ok(ApiResponse<BusinessOwnerResultDto>.Ok(await _platform.AddBusinessAdminAsync(id, request)));

        /// <summary>Resets a business user's password to a temporary one (returned once), unlocks them and signs them out.</summary>
        [Authorize(Policy = "PERMISSION:Platform.Support")]
        [HttpPost("businesses/{id:guid}/users/{userId:guid}/reset-password")]
        public async Task<IActionResult> ResetUserPassword(Guid id, Guid userId) =>
            Ok(ApiResponse<CreatedAccountDto>.Ok(await _platform.ResetBusinessUserPasswordAsync(id, userId)));

        [Authorize(Policy = "PERMISSION:Platform.Support")]
        [HttpPost("businesses/{id:guid}/users/{userId:guid}/unlock")]
        public async Task<IActionResult> UnlockUser(Guid id, Guid userId) =>
            Ok(ApiResponse<UserDto>.Ok(await _platform.UnlockBusinessUserAsync(id, userId)));

        /// <summary>A business's audit log (businessId given), or the platform's own events (registrations, super users).</summary>
        [Authorize(Policy = "PERMISSION:Platform.Support")]
        [HttpGet("audit-log")]
        public async Task<IActionResult> GetAuditLog([FromQuery] Guid? businessId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
            [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] int utcOffsetMinutes = 180,
            [FromQuery] string? eventType = null, [FromQuery] Guid? actorUserId = null, [FromQuery] string? search = null)
        {
            var paged = await _platform.GetAuditLogAsync(businessId, page, pageSize, from, to, utcOffsetMinutes, eventType, actorUserId, search);
            var meta = new PagingMeta(paged.Page, paged.PageSize, paged.TotalCount, paged.TotalPages);
            return Ok(ApiResponse<IEnumerable<AuditLogEntryDto>>.Ok(paged.Items, meta));
        }

        // ---- Super users ----

        [Authorize(Policy = "PERMISSION:Platform.Users.Manage")]
        [HttpGet("super-users")]
        public async Task<IActionResult> GetSuperUsers() =>
            Ok(ApiResponse<IEnumerable<UserDto>>.Ok(await _platform.GetSuperUsersAsync()));

        /// <summary>Creates another super user — always a new account, never one that works in a business.</summary>
        [Authorize(Policy = "PERMISSION:Platform.Users.Manage")]
        [HttpPost("super-users")]
        public async Task<IActionResult> CreateSuperUser(CreateSuperUserRequestDto request) =>
            Ok(ApiResponse<CreatedAccountDto>.Ok(await _platform.CreateSuperUserAsync(request)));

        [Authorize(Policy = "PERMISSION:Platform.Users.Manage")]
        [HttpPost("super-users/{userId:guid}/activate")]
        public async Task<IActionResult> ActivateSuperUser(Guid userId) =>
            Ok(ApiResponse<UserDto>.Ok(await _platform.SetSuperUserActiveAsync(userId, true)));

        [Authorize(Policy = "PERMISSION:Platform.Users.Manage")]
        [HttpPost("super-users/{userId:guid}/deactivate")]
        public async Task<IActionResult> DeactivateSuperUser(Guid userId) =>
            Ok(ApiResponse<UserDto>.Ok(await _platform.SetSuperUserActiveAsync(userId, false)));
    }
}
