using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace UltimatePos.Api.Auth
{
    public class PermissionPolicyProvider : IAuthorizationPolicyProvider
    {
        private const string prefix = "PERMISSION:";

        public DefaultAuthorizationPolicyProvider fallBackPolicyProvider { get; }


        public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
            => fallBackPolicyProvider = new DefaultAuthorizationPolicyProvider(options);

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => fallBackPolicyProvider.GetDefaultPolicyAsync();
        public Task<AuthorizationPolicy> GetFallbackPolicyAsync() => fallBackPolicyProvider.GetFallbackPolicyAsync();


        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) {

            if (policyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {

                var permission = policyName[prefix.Length..];
                var policy = new AuthorizationPolicyBuilder().AddRequirements(new PermissionRequirement(permission));

                return Task.FromResult<AuthorizationPolicy?>(policy.Build());

            }

            return fallBackPolicyProvider.GetPolicyAsync(policyName);   
        }
    }
}
