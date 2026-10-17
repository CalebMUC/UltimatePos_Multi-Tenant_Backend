using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace UltimatePos.Api.Auth
{
    public static class PermissionAuthorizationExtensions
    {
        // Must match PermissionPolicyProvider, which turns "PERMISSION:<name>" into a PermissionRequirement.
        private const string Prefix = "PERMISSION:";

        /// <summary>
        /// Whether the user holds a permission, decided by the same policy and handler that [Authorize(Policy = "PERMISSION:...")]
        /// uses. For endpoints whose RESPONSE depends on a permission (a section shown or left out, a cost field hidden),
        /// as opposed to the whole endpoint being allowed or denied.
        /// </summary>
        public static async Task<bool> HasPermissionAsync(this IAuthorizationService authorization, ClaimsPrincipal user, string permission) =>
            (await authorization.AuthorizeAsync(user, null, Prefix + permission)).Succeeded;
    }
}
