using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Identity;
using UltimatePos.Application.Identity.Dtos;

namespace UltimatePos.Api.Controllers;

[ApiController]
[Route("api/v1/roles")]
public class RolesController : ControllerBase
{
    private readonly AuthService _authService;
    public RolesController(AuthService authService) => _authService = authService;

    [Authorize(Policy = "PERMISSION:Roles.View")]
    [HttpGet]
    public async Task<IActionResult> GetRoles()
    {
        var result = await _authService.GetRolesAsync();
        return Ok(ApiResponse<IEnumerable<RoleDto>>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Roles.View")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetRole(Guid id)
    {
        var result = await _authService.GetRoleByIdAsync(id);
        return Ok(ApiResponse<RoleDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Roles.Create")]
    [HttpPost]
    public async Task<IActionResult> CreateRole(CreateRoleRequestDto request)
    {
        var result = await _authService.CreateRoleAsync(request);
        return Ok(ApiResponse<RoleDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Roles.Update")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateRole(Guid id, UpdateRoleRequestDto request)
    {
        var result = await _authService.UpdateRoleAsync(id, request);
        return Ok(ApiResponse<RoleDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Roles.Deactivate")]
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        var result = await _authService.SetRoleActiveStatusAsync(id, true);
        return Ok(ApiResponse<RoleDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Roles.Deactivate")]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var result = await _authService.SetRoleActiveStatusAsync(id, false);
        return Ok(ApiResponse<RoleDto>.Ok(result));
    }

    // Full replace of the role's permission set. GET /api/v1/permissions/modules
    // is what a client uses to build the picker that produces this PermissionIds list.
    [Authorize(Policy = "PERMISSION:Roles.AssignPermissions")]
    [HttpPut("{id:guid}/permissions")]
    public async Task<IActionResult> AssignPermissions(Guid id, AssignPermissionsRequestDto request)
    {
        var result = await _authService.AssignPermissionsToRoleAsync(id, request);
        return Ok(ApiResponse<RoleDto>.Ok(result));
    }
}