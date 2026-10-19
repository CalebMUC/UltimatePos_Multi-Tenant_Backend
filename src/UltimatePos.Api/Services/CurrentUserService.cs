using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Infrastructure.Auth;

namespace UltimatePos.Api.Services
{
    public class CurrentUserService : ICurrentUser
    {
        private readonly IHttpContextAccessor _accessor;

        public CurrentUserService(IHttpContextAccessor accessor) => _accessor = accessor;

        private ClaimsPrincipal? User => _accessor.HttpContext?.User;

        /// <summary>
        /// The signed-in user, or null. JwtBearer maps the token's "sub" claim to NameIdentifier on the way in, so both are read —
        /// reading only "sub" found nothing and stamped Guid.Empty as the author of every record.
        /// </summary>
        public Guid? UserId
        {
            get
            {
                var value = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                return Guid.TryParse(value, out var userId) && userId != Guid.Empty ? userId : null;
            }
        }

        public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

        public Guid? BusinessId =>
            Guid.TryParse(User?.FindFirst(JwtTokenService.BusinessClaim)?.Value, out var businessId) && businessId != Guid.Empty ? businessId : null;

        public Guid? BranchId =>
            Guid.TryParse(User?.FindFirst(JwtTokenService.BranchClaim)?.Value, out var branchId) && branchId != Guid.Empty ? branchId : null;

        public IReadOnlyList<Guid> BranchIds =>
            User?.FindAll(JwtTokenService.BranchesClaim).Select(c => Guid.TryParse(c.Value, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty).Distinct().ToList() ?? new List<Guid>();

        public bool HasPermission(string permission) => User?.HasClaim("Permission", permission) ?? false;
    }
}
