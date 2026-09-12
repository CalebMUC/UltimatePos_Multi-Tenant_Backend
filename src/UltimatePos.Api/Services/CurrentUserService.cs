using System.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using UltimatePos.Application.Common.Interfaces;



namespace UltimatePos.Api.Services
{
    
    public class CurrentUserService : ICurrentUser
    {
        private readonly IHttpContextAccessor _accessor;
        public CurrentUserService(IHttpContextAccessor accessor) => _accessor = accessor;

        public Guid? UserId
        {
            get { 
                var sub = _accessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                return Guid.TryParse(sub,out var userId) ? userId : Guid.Empty;
            }
        }

        public bool IsAuthenticated => _accessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    }
}
