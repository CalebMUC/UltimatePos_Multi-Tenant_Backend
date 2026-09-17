using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Business;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Common.Dtos;

namespace UltimatePos.Api.Controllers;

[ApiController]
[Route("api/v1/businesses")]
public class BusinessesController : ControllerBase
{
    private readonly BusinessService _businessService;
    public BusinessesController(BusinessService businessService) => _businessService = businessService;

    [Authorize(Policy = "PERMISSION:Businesses.Register")]
    [HttpPost]
    public async Task<IActionResult> Register(RegisterBusinessRequestDto request)
    {
        var result = await _businessService.RegisterBusinessAsync(request);
        return Ok(ApiResponse<BusinessDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Businesses.View")]
    [HttpGet]
    public async Task<IActionResult> GetBusinesses()
    {
        var result = await _businessService.GetBusinessesAsync();
        return Ok(ApiResponse<IEnumerable<BusinessDto>>.Ok(result));
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

    [Authorize(Policy = "PERMISSION:Businesses.Deactivate")]
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        var result = await _businessService.SetBusinessActiveStatusAsync(id, true);
        return Ok(ApiResponse<BusinessDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Businesses.Deactivate")]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var result = await _businessService.SetBusinessActiveStatusAsync(id, false);
        return Ok(ApiResponse<BusinessDto>.Ok(result));
    }
}