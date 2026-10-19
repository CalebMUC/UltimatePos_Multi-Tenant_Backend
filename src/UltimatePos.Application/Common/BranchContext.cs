using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Application.Common
{
    public sealed class BranchContext : IBranchContext
    {
        /// <summary>Holders see and work in every branch of the business (owners, managers, the business Admin role).</summary>
        public const string AccessAllPermission = "Branches.AccessAll";

        private readonly ICurrentUser _currentUser;
        private readonly Stack<Guid> _active = new();
        private readonly Stack<Guid> _narrowed = new();

        public BranchContext(ICurrentUser currentUser) => _currentUser = currentUser;

        // A read narrowed to one branch (?branchId=) also reads "at" that branch: its stock level, its average costs.
        public Guid? ActiveBranchId => _active.Count > 0 ? _active.Peek() : _narrowed.Count > 0 ? _narrowed.Peek() : _currentUser.BranchId;

        public Guid RequireActiveBranchId() =>
            ActiveBranchId ?? throw new ForbiddenException("No branch is selected. Sign in to a branch, or switch to one, first.");

        // System code (no signed-in user: webhooks, workers) acts for the whole business, like a branch-wide manager.
        public bool Unrestricted => _narrowed.Count == 0 && (_currentUser.UserId is null || _currentUser.HasPermission(AccessAllPermission));

        public IReadOnlyList<Guid> VisibleBranchIds => _narrowed.Count > 0 ? new[] { _narrowed.Peek() } : _currentUser.BranchIds;

        public bool CanSee(Guid branchId) => Unrestricted || VisibleBranchIds.Contains(branchId);

        public IDisposable Use(Guid branchId) => Push(_active, branchId);

        public IDisposable Narrow(Guid branchId) => Push(_narrowed, branchId);

        private static IDisposable Push(Stack<Guid> stack, Guid branchId)
        {
            if (branchId == Guid.Empty)
                throw new ArgumentException("A branch id is required.", nameof(branchId));
            stack.Push(branchId);
            return new Scope(stack);
        }

        private sealed class Scope : IDisposable
        {
            private readonly Stack<Guid> _stack;
            private bool _disposed;
            public Scope(Stack<Guid> stack) => _stack = stack;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _stack.Pop();
            }
        }
    }
}
