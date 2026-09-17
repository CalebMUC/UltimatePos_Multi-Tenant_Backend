using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Identity;
using UltimatePos.Application.Identity.Dtos;

namespace UltimatePos.Api.Controllers;

[ApiController]
[Route("api/v1/permissions")]
public class PermissionsController : ControllerBase
{
    private readonly AuthService _authService;
    public PermissionsController(AuthService authService) => _authService = authService;

    [Authorize(Policy = "PERMISSION:Permissions.View")]
    [HttpGet]
    public async Task<IActionResult> GetPermissions()
    {
        var result = await _authService.GetPermissionsAsync();
        return Ok(ApiResponse<IEnumerable<PermissionDto>>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Permissions.View")]
    [HttpGet("modules")]
    public async Task<IActionResult> GetModules()
    {
        var result = await _authService.GetPermissionModulesAsync();
        return Ok(ApiResponse<IEnumerable<PermissionModuleDto>>.Ok(result));
    }
}