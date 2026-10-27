using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Common.Dtos;

namespace UltimatePos.Api.Controllers;

/// <summary>The brands products are sold under (Soko, Pembe), for telling the versions of one item apart and filtering by them.</summary>
[ApiController]
[Route("api/v1/brands")]
public class BrandsController : ControllerBase
{
    private readonly CatalogService _catalogService;
    public BrandsController(CatalogService catalogService) => _catalogService = catalogService;

    /// <summary>Every brand by name, with how many products carry it. search matches the name; isActive filters.</summary>
    [Authorize(Policy = "PERMISSION:Products.View")]
    [HttpGet]
    public async Task<IActionResult> GetBrands([FromQuery] string? search = null, [FromQuery] bool? isActive = null)
    {
        var result = await _catalogService.GetBrandsAsync(search, isActive);
        return Ok(ApiResponse<IEnumerable<BrandDto>>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.ManageBrands")]
    [HttpPost]
    public async Task<IActionResult> CreateBrand(CreateBrandRequestDto request)
    {
        var result = await _catalogService.CreateBrandAsync(request);
        return Ok(ApiResponse<BrandDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.ManageBrands")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateBrand(Guid id, UpdateBrandRequestDto request)
    {
        var result = await _catalogService.UpdateBrandAsync(id, request);
        return Ok(ApiResponse<BrandDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.ManageBrands")]
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        var result = await _catalogService.SetBrandActiveStatusAsync(id, true);
        return Ok(ApiResponse<BrandDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.ManageBrands")]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var result = await _catalogService.SetBrandActiveStatusAsync(id, false);
        return Ok(ApiResponse<BrandDto>.Ok(result));
    }
}
