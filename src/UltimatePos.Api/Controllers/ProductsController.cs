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
    public async Task<IActionResult> GetProducts(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] Guid? categoryId = null, [FromQuery] ItemType? itemType = null)
    {
        var paged = await _catalogService.GetProductsAsync(page, pageSize, categoryId, itemType);
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
}