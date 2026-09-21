using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Common.Dtos;

namespace UltimatePos.Api.Controllers;

[ApiController]
[Route("api/v1/units-of-measure")]
public class UnitsOfMeasureController : ControllerBase
{
    private readonly CatalogService _catalogService;
    public UnitsOfMeasureController(CatalogService catalogService) => _catalogService = catalogService;

    [Authorize(Policy = "PERMISSION:Units.View")]
    [HttpGet("get-units")]
    public async Task<IActionResult> GetUnits()
    {
        var result = await _catalogService.GetUnitsOfMeasureAsync();
        return Ok(ApiResponse<IEnumerable<UnitOfMeasureDto>>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Units.Create")]
    [HttpPost("create-unit")]
    public async Task<IActionResult> CreateUnit(CreateUnitOfMeasureRequestDto request)
    {
        var result = await _catalogService.CreateUnitOfMeasureAsync(request);
        return Ok(ApiResponse<UnitOfMeasureDto>.Ok(result));
    }
}