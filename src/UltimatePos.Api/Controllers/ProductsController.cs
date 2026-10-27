using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Domain.Enums;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Api.Controllers;

[ApiController]
[Route("api/v1/products")]
public class ProductsController : ControllerBase
{
    private readonly CatalogService _catalogService;
    public ProductsController(CatalogService catalogService) => _catalogService = catalogService;

    [Authorize(Policy = "PERMISSION:Products.View")]
    [HttpGet("get-products")]
    // search matches name, SKU, barcode, generic name and brand. sortBy: name (default), sku, createdAt, category; sortDirection: asc | desc.
    public async Task<IActionResult> GetProducts(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] Guid? categoryId = null, [FromQuery] ItemType? itemType = null,
        [FromQuery] string? search = null, [FromQuery] bool? isActive = null,
        [FromQuery] bool? isBatchTracked = null, [FromQuery] bool? requiresPrescription = null,
        [FromQuery] string? sortBy = null, [FromQuery] string? sortDirection = null,
        [FromQuery] Guid? brandId = null)
    {
        var paged = await _catalogService.GetProductsAsync(page, pageSize,
            new ProductQuery(categoryId, itemType, search, isActive, isBatchTracked, requiresPrescription, sortBy, sortDirection, brandId));
        var meta = new PagingMeta(paged.Page, paged.PageSize, paged.TotalCount, paged.TotalPages);
        return Ok(ApiResponse<IEnumerable<ProductDto>>.Ok(paged.Items, meta));
    }

    [Authorize(Policy = "PERMISSION:Products.View")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetProduct(Guid id)
    {
        var result = await _catalogService.GetProductByIdAsync(id);
        return Ok(ApiResponse<ProductDetailDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.Create")]
    [HttpPost("create-product")]
    public async Task<IActionResult> CreateProduct(CreateProductRequestDto request)
    {
        var result = await _catalogService.CreateProductAsync(request);
        return Ok(ApiResponse<ProductDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.Update")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateProduct(Guid id, UpdateProductRequestDto request)
    {
        var result = await _catalogService.UpdateProductAsync(id, request);
        return Ok(ApiResponse<ProductDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.ManagePricing")]
    [HttpGet("{id:guid}/price-tiers")]
    public async Task<IActionResult> GetPriceTiers(Guid id)
    {
        var result = await _catalogService.GetPriceTiersAsync(id);
        return Ok(ApiResponse<IEnumerable<ProductPriceTierDto>>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.ManagePricing")]
    [HttpPost("{id:guid}/price-tiers")]
    public async Task<IActionResult> AddPriceTier(Guid id, AddPriceTierRequestDto request)
    {
        var result = await _catalogService.AddPriceTierAsync(id, request);
        return Ok(ApiResponse<ProductPriceTierDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.BulkImport")]
    [HttpGet("bulk-upload/template")]
    public IActionResult DownloadTemplate()
    {
        var bytes = _catalogService.GenerateProductImportTemplate();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "products-template.xlsx");
    }

    [Authorize(Policy = "PERMISSION:Products.BulkImport")]
    [HttpPost("bulk-upload")]
    public async Task<IActionResult> BulkUpload(IFormFile file, [FromQuery] bool dryRun = true)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("EMPTY_FILE", "No file uploaded."));

        await using var stream = file.OpenReadStream();
        var result = await _catalogService.BulkImportProductsAsync(stream, dryRun);
        return Ok(ApiResponse<ProductBulkImportResultDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.ManageUnitConversions")]
    [HttpGet("{id:guid}/unit-conversions")]
    public async Task<IActionResult> GetUnitConversions(Guid id)
    {
        var result = await _catalogService.GetUnitConversionsAsync(id);
        return Ok(ApiResponse<IEnumerable<ProductUnitConversionDto>>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.ManageUnitConversions")]
    [HttpPost("{id:guid}/unit-conversions")]
    public async Task<IActionResult> AddUnitConversion(Guid id, CreateProductUnitConversionRequestDto request)
    {
        var result = await _catalogService.AddUnitConversionAsync(id, request);
        return Ok(ApiResponse<ProductUnitConversionDto>.Ok(result));
    }

    /// <summary>Scanner lookup: the product with this exact barcode or SKU.</summary>
    [Authorize(Policy = "PERMISSION:Products.View")]
    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery] string code)
    {
        var result = await _catalogService.GetProductByCodeAsync(code);
        return Ok(ApiResponse<ProductDetailDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.Deactivate")]
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        var result = await _catalogService.SetProductActiveStatusAsync(id, true);
        return Ok(ApiResponse<ProductDto>.Ok(result));
    }

    /// <summary>An inactive product cannot be sold or purchased; its history stays.</summary>
    [Authorize(Policy = "PERMISSION:Products.Deactivate")]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var result = await _catalogService.SetProductActiveStatusAsync(id, false);
        return Ok(ApiResponse<ProductDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.ManageUnitConversions")]
    [HttpPut("{id:guid}/unit-conversions/{conversionId:guid}")]
    public async Task<IActionResult> UpdateUnitConversion(Guid id, Guid conversionId, UpdateProductUnitConversionRequestDto request)
    {
        var result = await _catalogService.UpdateUnitConversionAsync(id, conversionId, request);
        return Ok(ApiResponse<ProductUnitConversionDto>.Ok(result));
    }

    /// <summary>The container sold with one of this pack when the customer has none (an empty 20L container with a 20L refill). Null clears it.</summary>
    [Authorize(Policy = "PERMISSION:Products.ManageUnitConversions")]
    [HttpPut("{id:guid}/unit-conversions/{conversionId:guid}/container")]
    public async Task<IActionResult> SetUnitConversionContainer(Guid id, Guid conversionId, SetConversionContainerRequestDto request)
    {
        var result = await _catalogService.SetUnitConversionContainerAsync(id, conversionId, request);
        return Ok(ApiResponse<ProductUnitConversionDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.ManageUnitConversions")]
    [HttpPost("{id:guid}/unit-conversions/{conversionId:guid}/activate")]
    public async Task<IActionResult> ActivateUnitConversion(Guid id, Guid conversionId)
    {
        var result = await _catalogService.SetUnitConversionActiveStatusAsync(id, conversionId, true);
        return Ok(ApiResponse<ProductUnitConversionDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.ManageUnitConversions")]
    [HttpPost("{id:guid}/unit-conversions/{conversionId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateUnitConversion(Guid id, Guid conversionId)
    {
        var result = await _catalogService.SetUnitConversionActiveStatusAsync(id, conversionId, false);
        return Ok(ApiResponse<ProductUnitConversionDto>.Ok(result));
    }
}