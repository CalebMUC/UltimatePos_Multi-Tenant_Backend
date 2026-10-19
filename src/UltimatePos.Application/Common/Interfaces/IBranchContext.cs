namespace UltimatePos.Application.Common.Interfaces
{
    /// <summary>
    /// Which branch the request works in, and which branches it may see. Writes happen at the ACTIVE branch (from the access token,
    /// or entered explicitly by system code). Reads cover the VISIBLE branches: every branch for users with Branches.AccessAll and for
    /// system code, otherwise the branches the user is assigned to — narrowed to one with <see cref="Narrow"/> (a report filter).
    /// </summary>
    public interface IBranchContext
    {
        Guid? ActiveBranchId { get; }

        /// <summary>The active branch, or a ForbiddenException asking the user to choose one.</summary>
        Guid RequireActiveBranchId();

        /// <summary>True when every branch of the business is visible (and no narrowing applies).</summary>
        bool Unrestricted { get; }

        /// <summary>The visible branches when not <see cref="Unrestricted"/>.</summary>
        IReadOnlyList<Guid> VisibleBranchIds { get; }

        bool CanSee(Guid branchId);

        /// <summary>Makes <paramref name="branchId"/> the active branch until disposed (system code acting at a branch).</summary>
        IDisposable Use(Guid branchId);

        /// <summary>Limits reads to <paramref name="branchId"/> until disposed. The caller checks the branch is visible first.</summary>
        IDisposable Narrow(Guid branchId);
    }
}
