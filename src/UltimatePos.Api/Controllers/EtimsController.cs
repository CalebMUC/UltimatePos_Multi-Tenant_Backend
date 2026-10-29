using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Etims;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Api.Controllers;

/// <summary>
/// KRA eTIMS (OSCU). Each branch that reports to KRA has a device: register it once (KRA issues its communication key), download
/// KRA's code and item classification lists, and give products their classification. From then on the branch's sales, returns,
/// purchases and stock movements are reported automatically; the submissions show what was sent and what KRA said.
/// </summary>
[ApiController]
[Route("api/v1/etims")]
public class EtimsController : ControllerBase
{
    private readonly EtimsService _etims;
    public EtimsController(EtimsService etims) => _etims = etims;

    // ---- Devices ----

    [Authorize(Policy = "PERMISSION:Etims.View")]
    [HttpGet("devices")]
    public async Task<IActionResult> GetDevices() => Ok(ApiResponse<IEnumerable<EtimsDeviceDto>>.Ok(await _etims.GetDevicesAsync()));

    [Authorize(Policy = "PERMISSION:Etims.View")]
    [HttpGet("devices/{id:guid}")]
    public async Task<IActionResult> GetDevice(Guid id) => Ok(ApiResponse<EtimsDeviceDto>.Ok(await _etims.GetDeviceAsync(id)));

    /// <summary>Initialises the branch's device with KRA and starts reporting the branch from now.</summary>
    [Authorize(Policy = "PERMISSION:Etims.Manage")]
    [HttpPost("devices")]
    public async Task<IActionResult> RegisterDevice(RegisterEtimsDeviceRequestDto request)
    {
        var result = await _etims.RegisterDeviceAsync(request);
        return CreatedAtAction(nameof(GetDevice), new { id = result.EtimsDeviceId }, ApiResponse<EtimsDeviceDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Etims.Manage")]
    [HttpPut("devices/{id:guid}")]
    public async Task<IActionResult> UpdateDevice(Guid id, UpdateEtimsDeviceRequestDto request) =>
        Ok(ApiResponse<EtimsDeviceDto>.Ok(await _etims.UpdateDeviceAsync(id, request)));

    /// <summary>Queues what is new at the branch and sends what is due, now, instead of waiting for the worker.</summary>
    [Authorize(Policy = "PERMISSION:Etims.Manage")]
    [HttpPost("devices/{id:guid}/run")]
    public async Task<IActionResult> Run(Guid id, CancellationToken cancellationToken) =>
        Ok(ApiResponse<EtimsRunResultDto>.Ok(await _etims.RunNowAsync(id, cancellationToken)));

    // ---- KRA's lists ----

    /// <summary>Downloads KRA's code list, item classifications and notices, and keeps them for the lookups below.</summary>
    [Authorize(Policy = "PERMISSION:Etims.Manage")]
    [HttpPost("devices/{id:guid}/sync-codes")]
    public async Task<IActionResult> SyncCodes(Guid id, CancellationToken cancellationToken) =>
        Ok(ApiResponse<IEnumerable<EtimsReferenceSyncDto>>.Ok(await _etims.SyncReferenceDataAsync(id, cancellationToken)));

    /// <summary>KRA's codes as last downloaded. codeClass narrows to one list (e.g. 10 quantity units, 17 packaging units).</summary>
    [Authorize(Policy = "PERMISSION:Etims.View")]
    [HttpGet("devices/{id:guid}/codes")]
    public async Task<IActionResult> GetCodes(Guid id, [FromQuery] string? codeClass = null) =>
        Ok(ApiResponse<IEnumerable<EtimsCodeDto>>.Ok(await _etims.GetCodesAsync(id, codeClass)));

    /// <summary>Searches KRA's item classifications by code prefix or name.</summary>
    [Authorize(Policy = "PERMISSION:Etims.View")]
    [HttpGet("devices/{id:guid}/item-classes")]
    public async Task<IActionResult> SearchItemClasses(Guid id, [FromQuery] string? search = null, [FromQuery] int take = 50) =>
        Ok(ApiResponse<IEnumerable<EtimsItemClassDto>>.Ok(await _etims.SearchItemClassesAsync(id, search, take)));

    /// <summary>
    /// A read from KRA, passed through. what: taxpayer, branches, notices, items, purchases (invoices suppliers issued to the
    /// business), imports, sales, stock-moves (argument = lastReqDt, yyyyMMddHHmmss, optional); customer-pin (argument = the PIN);
    /// invoice (argument = the invoice number).
    /// </summary>
    [Authorize(Policy = "PERMISSION:Etims.View")]
    [HttpGet("devices/{id:guid}/kra/{what}")]
    public async Task<IActionResult> Lookup(Guid id, string what, [FromQuery] string? argument, CancellationToken cancellationToken) =>
        Ok(ApiResponse<EtimsLookupDto>.Ok(await _etims.LookupAsync(id, what, argument, cancellationToken)));

    [Authorize(Policy = "PERMISSION:Etims.Manage")]
    [HttpPost("devices/{id:guid}/customers/{customerId:guid}")]
    public async Task<IActionResult> SendCustomer(Guid id, Guid customerId, CancellationToken cancellationToken) =>
        Ok(ApiResponse<EtimsLookupDto>.Ok(await _etims.SendCustomerAsync(id, customerId, cancellationToken)));

    [Authorize(Policy = "PERMISSION:Etims.Manage")]
    [HttpPost("devices/{id:guid}/branch-users")]
    public async Task<IActionResult> SendBranchUser(Guid id, RegisterEtimsBranchUserRequestDto request, CancellationToken cancellationToken) =>
        Ok(ApiResponse<EtimsLookupDto>.Ok(await _etims.SendBranchUserAsync(id, request, cancellationToken)));

    [Authorize(Policy = "PERMISSION:Etims.Manage")]
    [HttpPost("devices/{id:guid}/imported-items")]
    public async Task<IActionResult> ConvertImportedItem(Guid id, ConvertEtimsImportedItemRequestDto request, CancellationToken cancellationToken) =>
        Ok(ApiResponse<EtimsLookupDto>.Ok(await _etims.ConvertImportedItemAsync(id, request, cancellationToken)));

    // ---- How products are described ----

    [Authorize(Policy = "PERMISSION:Etims.View")]
    [HttpGet("products/{productId:guid}")]
    public async Task<IActionResult> GetProductProfile(Guid productId) =>
        Ok(ApiResponse<EtimsProductProfileDto>.Ok(await _etims.GetProductProfileAsync(productId)));

    [Authorize(Policy = "PERMISSION:Etims.Manage")]
    [HttpPut("products/{productId:guid}")]
    public async Task<IActionResult> UpdateProductProfile(Guid productId, UpdateEtimsProductProfileRequestDto request) =>
        Ok(ApiResponse<EtimsProductProfileDto>.Ok(await _etims.UpdateProductProfileAsync(productId, request)));

    [Authorize(Policy = "PERMISSION:Etims.Manage")]
    [HttpPut("categories/{categoryId:guid}")]
    public async Task<IActionResult> SetCategoryClass(Guid categoryId, SetEtimsCategoryClassRequestDto request)
    {
        await _etims.SetCategoryClassAsync(categoryId, request);
        return NoContent();
    }

    // ---- What was sent ----

    [Authorize(Policy = "PERMISSION:Etims.View")]
    [HttpGet("submissions")]
    public async Task<IActionResult> GetSubmissions([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? deviceId = null,
        [FromQuery] EtimsSubmissionStatus? status = null, [FromQuery] EtimsDocumentType? documentType = null, [FromQuery] Guid? sourceId = null)
    {
        var paged = await _etims.GetSubmissionsAsync(page, pageSize, new EtimsSubmissionQuery(deviceId, status, documentType, sourceId));
        var meta = new PagingMeta(paged.Page, paged.PageSize, paged.TotalCount, paged.TotalPages);
        return Ok(ApiResponse<IEnumerable<EtimsSubmissionDto>>.Ok(paged.Items, meta));
    }

    /// <summary>One submission with the exact request sent and KRA's reply.</summary>
    [Authorize(Policy = "PERMISSION:Etims.View")]
    [HttpGet("submissions/{id:guid}")]
    public async Task<IActionResult> GetSubmission(Guid id) =>
        Ok(ApiResponse<EtimsSubmissionDetailDto>.Ok(await _etims.GetSubmissionAsync(id)));

    [Authorize(Policy = "PERMISSION:Etims.Manage")]
    [HttpPost("submissions/{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id) => Ok(ApiResponse<EtimsSubmissionDto>.Ok(await _etims.RetryAsync(id)));

    /// <summary>The sale's KRA signature and QR link, for printing on its receipt. Cashiers who can view sales can read it.</summary>
    [Authorize(Policy = "PERMISSION:Sales.View")]
    [HttpGet("sales/{saleId:guid}/receipt")]
    public async Task<IActionResult> GetSaleReceipt(Guid saleId) =>
        Ok(ApiResponse<EtimsReceiptDto>.Ok(await _etims.GetSaleReceiptAsync(saleId)));
}
