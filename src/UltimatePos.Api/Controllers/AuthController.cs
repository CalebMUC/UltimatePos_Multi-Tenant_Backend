using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Identity;
using UltimatePos.Application.Identity.Dtos;

namespace UltimatePos.Api.Controllers
{
    [ApiController]
    [Route("api/v1/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _authService;

        public AuthController(AuthService authService)
        {
            _authService = authService;
        }
        [AllowAnonymous]
        [HttpPost("Login")]
        public async Task<IActionResult> Login(LoginRequest request) { 
            var result = await _authService.LoginAsync(request);
            return Ok(ApiResponse<LoginResponseDto>.Ok(result));
        }
        [Authorize(Policy ="PERMISSION:Users.Create")]
        [HttpPost("Register")]
        public async Task<IActionResult> Register(RegisterRequest request) { 
            var result = await _authService.RegisterAsync(request);
            return Ok(ApiResponse<UserDto>.Ok(result));
        }
    }
}
