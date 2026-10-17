using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Application.Common
{
    public sealed class TenantContext : ITenantContext
    {
        private readonly ICurrentUser _currentUser;
        private readonly Stack<Guid> _overrides = new();

        public TenantContext(ICurrentUser currentUser) => _currentUser = currentUser;

        public Guid? BusinessId => _overrides.Count > 0 ? _overrides.Peek() : _currentUser.BusinessId;

        public Guid RequireBusinessId() =>
            BusinessId ?? throw new ForbiddenException("No business is selected. Sign in to a business, or switch to one, first.");

        public IDisposable Use(Guid businessId)
        {
            if (businessId == Guid.Empty)
                throw new ArgumentException("A business id is required.", nameof(businessId));

            _overrides.Push(businessId);
            return new Scope(_overrides);
        }

        private sealed class Scope : IDisposable
        {
            private readonly Stack<Guid> _overrides;
            private bool _disposed;

            public Scope(Stack<Guid> overrides) => _overrides = overrides;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _overrides.Pop();
            }
        }
    }
}
