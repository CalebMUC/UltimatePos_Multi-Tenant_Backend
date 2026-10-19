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

        private string? Device => Request.Headers.UserAgent.ToString();
        private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

        /// <summary>Signs in to a business (the default one when businessId is left out). Returns an access token and a refresh token.</summary>
        [AllowAnonymous]
        [HttpPost("Login")]
        public async Task<IActionResult> Login(LoginRequest request) {
            var result = await _authService.LoginAsync(request, Device, Ip);
            return Ok(ApiResponse<LoginResponseDto>.Ok(result));
        }

        /// <summary>Swaps a refresh token for a new token pair. The old refresh token stops working (rotation).</summary>
        [AllowAnonymous]
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh(RefreshTokenRequest request)
        {
            var result = await _authService.RefreshAsync(request, Device, Ip);
            return Ok(ApiResponse<LoginResponseDto>.Ok(result));
        }

        /// <summary>Issues tokens for another business the user belongs to.</summary>
        [HttpPost("switch-business")]
        public async Task<IActionResult> SwitchBusiness(SwitchBusinessRequest request)
        {
            var result = await _authService.SwitchBusinessAsync(request, Device, Ip);
            return Ok(ApiResponse<LoginResponseDto>.Ok(result));
        }

        /// <summary>Issues tokens for another branch of the business signed in to (one the user works at, or any with Branches.AccessAll).</summary>
        [HttpPost("switch-branch")]
        public async Task<IActionResult> SwitchBranch(SwitchBranchRequest request)
        {
            var result = await _authService.SwitchBranchAsync(request, Device, Ip);
            return Ok(ApiResponse<LoginResponseDto>.Ok(result));
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout(LogoutRequest request)
        {
            await _authService.LogoutAsync(request);
            return Ok(ApiResponse<object>.Ok(new { }));
        }

        /// <summary>The signed-in user, their roles and permissions in the current business (read fresh), and their businesses.</summary>
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var result = await _authService.GetMeAsync();
            return Ok(ApiResponse<MeDto>.Ok(result));
        }

        /// <summary>Changes the caller's own password and signs out their other devices.</summary>
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
        {
            await _authService.ChangePasswordAsync(request);
            return Ok(ApiResponse<object>.Ok(new { }));
        }

        /// <summary>Creates a user in the business signed in to, with one of its roles.</summary>
        [Authorize(Policy ="PERMISSION:Users.Create")]
        [HttpPost("Register")]
        public async Task<IActionResult> Register(RegisterRequest request) {
            var result = await _authService.RegisterAsync(request);
            return Ok(ApiResponse<UserDto>.Ok(result));
        }
    }
}
