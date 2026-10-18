using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Business;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Platform;

namespace UltimatePos.Api.Controllers;

/// <summary>
/// Businesses as their own users see them (profile, licence), plus the registration and close/reopen routes, which are super-user
/// actions handled by PlatformService. The rest of the super-user area is under /api/v1/platform.
/// </summary>
[ApiController]
[Route("api/v1/businesses")]
public class BusinessController : ControllerBase
{
    private readonly BusinessService _businessService;
    private readonly PlatformService _platform;

    public BusinessController(BusinessService businessService, PlatformService platform)
    {
        _businessService = businessService;
        _platform = platform;
    }

    /// <summary>Super user: registers a business and hands it to its owner (who becomes its Admin). The super user does not join it.</summary>
    [Authorize(Policy = "PERMISSION:Businesses.Register")]
    [HttpPost]
    public async Task<IActionResult> Register(RegisterBusinessRequestDto request)
    {
        var result = await _platform.RegisterBusinessAsync(request);
        return Ok(ApiResponse<RegisterBusinessResultDto>.Ok(result));
    }

    /// <summary>The businesses the caller belongs to — or every business, for super users.</summary>
    [Authorize(Policy = "PERMISSION:Businesses.View")]
    [HttpGet]
    public async Task<IActionResult> GetBusinesses()
    {
        var result = await _businessService.GetBusinessesAsync();
        return Ok(ApiResponse<IEnumerable<BusinessDto>>.Ok(result));
    }

    /// <summary>The business signed in to — receipts, VAT registration and discount cap.</summary>
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentBusiness()
    {
        var result = await _businessService.GetCurrentBusinessAsync();
        return Ok(ApiResponse<BusinessDto>.Ok(result));
    }

    /// <summary>The signed-in business's licence: where it stands, and its history.</summary>
    [Authorize(Policy = "PERMISSION:Businesses.View")]
    [HttpGet("current/license")]
    public async Task<IActionResult> GetCurrentLicense()
    {
        var result = await _businessService.GetCurrentLicenseAsync();
        return Ok(ApiResponse<BusinessLicenseStatusDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Businesses.View")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetBusiness(Guid id)
    {
        var result = await _businessService.GetBusinessByIdAsync(id);
        return Ok(ApiResponse<BusinessDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Businesses.Update")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateBusiness(Guid id, UpdateBusinessRequestDto request)
    {
        var result = await _businessService.UpdateBusinessAsync(id, request);
        return Ok(ApiResponse<BusinessDto>.Ok(result));
    }

    /// <summary>Super user: reopens a closed business. Same as POST /platform/businesses/{id}/reopen.</summary>
    [Authorize(Policy = "PERMISSION:Businesses.Deactivate")]
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        var result = await _platform.ReopenAsync(id);
        return Ok(ApiResponse<BusinessSummaryDto>.Ok(result));
    }

    /// <summary>Super user: closes a business. Same as POST /platform/businesses/{id}/close.</summary>
    [Authorize(Policy = "PERMISSION:Businesses.Deactivate")]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var result = await _platform.CloseAsync(id, null);
        return Ok(ApiResponse<BusinessSummaryDto>.Ok(result));
    }
}
