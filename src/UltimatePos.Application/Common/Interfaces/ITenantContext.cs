using System;

namespace UltimatePos.Application.Common.Interfaces
{
    /// <summary>
    /// The business every query and insert in this request is scoped to. Normally the business in the caller's access token;
    /// code with no caller (M-Pesa webhooks, seeding, onboarding a new business) enters a business explicitly with <see cref="Use"/>.
    /// With no business in scope, business-owned data is invisible and cannot be written — the system fails closed.
    /// </summary>
    public interface ITenantContext
    {
        Guid? BusinessId { get; }

        /// <summary>The business in scope, or a ForbiddenException when there is none.</summary>
        Guid RequireBusinessId();

        /// <summary>Scopes everything until the returned handle is disposed to <paramref name="businessId"/>. Nests.</summary>
        IDisposable Use(Guid businessId);
    }
}
