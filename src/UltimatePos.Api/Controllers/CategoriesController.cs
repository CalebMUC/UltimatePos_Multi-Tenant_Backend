using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Common.Dtos;

namespace UltimatePos.Api.Controllers;

[ApiController]
[Route("api/v1/categories")]
public class CategoriesController : ControllerBase
{
    private readonly CatalogService _catalogService;
    public CategoriesController(CatalogService catalogService) => _catalogService = catalogService;

    [Authorize(Policy = "PERMISSION:Categories.View")]
    [HttpGet("get-categories")]
    public async Task<IActionResult> GetCategories()
    {
        var result = await _catalogService.GetCategoriesAsync();
        return Ok(ApiResponse<IEnumerable<CategoryDto>>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Categories.Create")]
    [HttpPost("create-category")]
    public async Task<IActionResult> CreateCategory(CreateCategoryRequestDto request)
    {
        var result = await _catalogService.CreateCategoryAsync(request);
        return Ok(ApiResponse<CategoryDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Categories.BulkImport")]
    [HttpGet("bulk-upload/template")]
    public IActionResult DownloadTemplate()
    {
        var bytes = _catalogService.GenerateCategoryImportTemplate();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "categories-template.xlsx");
    }

    [Authorize(Policy = "PERMISSION:Categories.BulkImport")]
    [HttpPost("bulk-upload")]
    public async Task<IActionResult> BulkUpload(IFormFile file, [FromQuery] bool dryRun = true)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("EMPTY_FILE", "No file uploaded."));

        await using var stream = file.OpenReadStream();
        var result = await _catalogService.BulkImportCategoriesAsync(stream, dryRun);
        return Ok(ApiResponse<CategoryBulkImportResultDto>.Ok(result));
    }
}