using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Identity;

namespace UltimatePos.Api.Controllers
{
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

        [Authorize(Policy = "PERMISSION:Roles.Create")]
        [HttpPost]
        public async Task<IActionResult> CreateRole(CreateRoleRequestDto request)
        {
            var result = await _authService.CreateRoleAsync(request);
            return Ok(ApiResponse<RoleDto>.Ok(result));
        }
    }
}
