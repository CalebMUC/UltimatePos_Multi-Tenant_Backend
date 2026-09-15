using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Identity;
using UltimatePos.Application.Identity.Dtos;

namespace UltimatePos.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
public class UsersController : ControllerBase
{
    private readonly AuthService _authService;
    public UsersController(AuthService authService) => _authService = authService;

    [Authorize(Policy = "PERMISSION:Users.View")]
    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        var result = await _authService.GetUsersAsync();
        return Ok(ApiResponse<IEnumerable<UserDto>>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Users.View")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetUser(Guid id)
    {
        var result = await _authService.GetUserByIdAsync(id);
        return Ok(ApiResponse<UserDto>.Ok(result));
    }

    // Assign and reassign are the same call — full replace of the user's role set.
    [Authorize(Policy = "PERMISSION:Users.AssignRoles")]
    [HttpPut("{id:guid}/roles")]
    public async Task<IActionResult> AssignRoles(Guid id, AssignRolesRequestDto request)
    {
        var result = await _authService.AssignRolesToUserAsync(id, request);
        return Ok(ApiResponse<UserDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Users.Deactivate")]
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        var result = await _authService.SetUserActiveStatusAsync(id, true);
        return Ok(ApiResponse<UserDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Users.Deactivate")]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var result = await _authService.SetUserActiveStatusAsync(id, false);
        return Ok(ApiResponse<UserDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Users.Lock")]
    [HttpPost("{id:guid}/lock")]
    public async Task<IActionResult> Lock(Guid id)
    {
        var result = await _authService.SetUserLockStatusAsync(id, true);
        return Ok(ApiResponse<UserDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Users.Lock")]
    [HttpPost("{id:guid}/unlock")]
    public async Task<IActionResult> Unlock(Guid id)
    {
        var result = await _authService.SetUserLockStatusAsync(id, false);
        return Ok(ApiResponse<UserDto>.Ok(result));
    }
}