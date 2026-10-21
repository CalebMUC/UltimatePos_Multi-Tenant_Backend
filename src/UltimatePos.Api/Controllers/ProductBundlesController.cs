using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Common.Dtos;

namespace UltimatePos.Api.Controllers;

/// <summary>
/// Bundles: several stocked products sold at one price, e.g. "20L Heavy-duty bottle + water" (20 L of water and one bottle, 1500) or
/// "Pack of 12 x 1L". Sold through POST /sales with <c>bundles</c>; each part leaves its own stock.
/// </summary>
[ApiController]
[Route("api/v1/product-bundles")]
public class ProductBundlesController : ControllerBase
{
    private readonly ProductBundleService _bundles;
    public ProductBundlesController(ProductBundleService bundles) => _bundles = bundles;

    /// <summary>By name. availableQuantity (users with Stock.View) is how many can be sold now at the branch signed in to.</summary>
    [Authorize(Policy = "PERMISSION:Products.View")]
    [HttpGet]
    public async Task<IActionResult> GetBundles([FromQuery] bool? isActive = null, [FromQuery] string? search = null)
    {
        var result = await _bundles.ListAsync(isActive, search);
        return Ok(ApiResponse<IEnumerable<ProductBundleDto>>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.View")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetBundle(Guid id)
    {
        var result = await _bundles.GetAsync(id);
        return Ok(ApiResponse<ProductBundleDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.ManageBundles")]
    [HttpPost]
    public async Task<IActionResult> CreateBundle(CreateProductBundleRequestDto request)
    {
        var result = await _bundles.CreateAsync(request);
        return CreatedAtAction(nameof(GetBundle), new { id = result.ProductBundleId }, ApiResponse<ProductBundleDto>.Ok(result));
    }

    /// <summary>Replaces name, prices and parts. Past sales are unchanged.</summary>
    [Authorize(Policy = "PERMISSION:Products.ManageBundles")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateBundle(Guid id, UpdateProductBundleRequestDto request)
    {
        var result = await _bundles.UpdateAsync(id, request);
        return Ok(ApiResponse<ProductBundleDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.ManageBundles")]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var result = await _bundles.SetActiveAsync(id, false);
        return Ok(ApiResponse<ProductBundleDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Products.ManageBundles")]
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        var result = await _bundles.SetActiveAsync(id, true);
        return Ok(ApiResponse<ProductBundleDto>.Ok(result));
    }
}
