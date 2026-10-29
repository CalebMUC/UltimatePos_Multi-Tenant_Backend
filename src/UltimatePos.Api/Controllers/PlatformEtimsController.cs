using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Etims;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Api.Controllers;

/// <summary>
/// KRA eTIMS for super users, as the integrator every business reports through: onboard a business's branches (register their
/// devices with KRA, download KRA's lists), and watch what KRA refused across every business. eTIMS itself is switched on per
/// business with PUT /api/v1/platform/businesses/{id}/etims.
/// </summary>
[ApiController]
[Route("api/v1/platform/etims")]
public class PlatformEtimsController : ControllerBase
{
    private readonly PlatformEtimsService _etims;
    public PlatformEtimsController(PlatformEtimsService etims) => _etims = etims;

    // ---- Across businesses ----

    /// <summary>Every business that reports to KRA: its devices, what is waiting, what failed or was refused, and when KRA last accepted something.</summary>
    [Authorize(Policy = "PERMISSION:Platform.Etims.View")]
    [HttpGet]
    public async Task<IActionResult> GetOverview() => Ok(ApiResponse<PlatformEtimsOverviewDto>.Ok(await _etims.GetOverviewAsync()));

    /// <summary>Submissions across every business, most recently attempted first. With no status: Failed and Rejected (what needs attention).</summary>
    [Authorize(Policy = "PERMISSION:Platform.Etims.View")]
    [HttpGet("submissions")]
    public async Task<IActionResult> GetSubmissions([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? businessId = null,
        [FromQuery] Guid? deviceId = null, [FromQuery] EtimsSubmissionStatus? status = null, [FromQuery] EtimsDocumentType? documentType = null)
    {
        var paged = await _etims.GetSubmissionsAsync(page, pageSize, new PlatformEtimsSubmissionQuery(businessId, deviceId, status, documentType));
        var meta = new PagingMeta(paged.Page, paged.PageSize, paged.TotalCount, paged.TotalPages);
        return Ok(ApiResponse<IEnumerable<PlatformEtimsSubmissionDto>>.Ok(paged.Items, meta));
    }

    /// <summary>One submission with the exact request sent and KRA's reply.</summary>
    [Authorize(Policy = "PERMISSION:Platform.Etims.View")]
    [HttpGet("submissions/{id:guid}")]
    public async Task<IActionResult> GetSubmission(Guid id) =>
        Ok(ApiResponse<EtimsSubmissionDetailDto>.Ok(await _etims.GetSubmissionAsync(id)));

    /// <summary>Sends a Failed or Rejected submission again on the next pass, with a fresh set of attempts.</summary>
    [Authorize(Policy = "PERMISSION:Platform.Etims.Manage")]
    [HttpPost("submissions/{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id) => Ok(ApiResponse<EtimsSubmissionDto>.Ok(await _etims.RetryAsync(id)));

    // ---- One business ----

    /// <summary>The business's branches, each with its device (null = not registered yet).</summary>
    [Authorize(Policy = "PERMISSION:Platform.Etims.View")]
    [HttpGet("businesses/{businessId:guid}")]
    public async Task<IActionResult> GetBusiness(Guid businessId) =>
        Ok(ApiResponse<PlatformEtimsBusinessDto>.Ok(await _etims.GetBusinessAsync(businessId)));

    /// <summary>Initialises a branch's device with KRA for the business and starts reporting the branch from now.</summary>
    [Authorize(Policy = "PERMISSION:Platform.Etims.Manage")]
    [HttpPost("businesses/{businessId:guid}/devices")]
    public async Task<IActionResult> RegisterDevice(Guid businessId, RegisterEtimsDeviceRequestDto request) =>
        Ok(ApiResponse<EtimsDeviceDto>.Ok(await _etims.RegisterDeviceAsync(businessId, request)));

    [Authorize(Policy = "PERMISSION:Platform.Etims.Manage")]
    [HttpPut("businesses/{businessId:guid}/devices/{deviceId:guid}")]
    public async Task<IActionResult> UpdateDevice(Guid businessId, Guid deviceId, UpdateEtimsDeviceRequestDto request) =>
        Ok(ApiResponse<EtimsDeviceDto>.Ok(await _etims.UpdateDeviceAsync(businessId, deviceId, request)));

    /// <summary>Downloads KRA's code list, item classifications and notices through the device.</summary>
    [Authorize(Policy = "PERMISSION:Platform.Etims.Manage")]
    [HttpPost("businesses/{businessId:guid}/devices/{deviceId:guid}/sync-codes")]
    public async Task<IActionResult> SyncCodes(Guid businessId, Guid deviceId, CancellationToken cancellationToken) =>
        Ok(ApiResponse<IEnumerable<EtimsReferenceSyncDto>>.Ok(await _etims.SyncReferenceDataAsync(businessId, deviceId, cancellationToken)));

    /// <summary>Searches KRA's item classifications (as last downloaded) by code prefix or name.</summary>
    [Authorize(Policy = "PERMISSION:Platform.Etims.View")]
    [HttpGet("businesses/{businessId:guid}/devices/{deviceId:guid}/item-classes")]
    public async Task<IActionResult> SearchItemClasses(Guid businessId, Guid deviceId, [FromQuery] string? search = null, [FromQuery] int take = 50) =>
        Ok(ApiResponse<IEnumerable<EtimsItemClassDto>>.Ok(await _etims.SearchItemClassesAsync(businessId, deviceId, search, take)));

    /// <summary>Queues what is new at the branch and sends what is due, now, instead of waiting for the worker.</summary>
    [Authorize(Policy = "PERMISSION:Platform.Etims.Manage")]
    [HttpPost("businesses/{businessId:guid}/devices/{deviceId:guid}/run")]
    public async Task<IActionResult> Run(Guid businessId, Guid deviceId, CancellationToken cancellationToken) =>
        Ok(ApiResponse<EtimsRunResultDto>.Ok(await _etims.RunNowAsync(businessId, deviceId, cancellationToken)));
}
