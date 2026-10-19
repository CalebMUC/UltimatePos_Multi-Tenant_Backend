namespace UltimatePos.Application.Branches
{
    /// <param name="BranchNumber">0–99; 0 is the head office.</param>
    /// <param name="KraBranchId">The same number as KRA eTIMS writes it: "00", "01", ...</param>
    public record BranchDto(Guid BranchId, int BranchNumber, string KraBranchId, string Name, string? Code, string? PhysicalAddress, string? County,
        string? PhoneNumber, string? Email, string? MpesaShortCode, bool IsHeadOffice, bool IsActive, DateTime CreatedAt);

    /// <param name="BranchNumber">Leave out to take the next free number (1, 2, ...). 0 is reserved for the head office.</param>
    public record CreateBranchRequestDto(string Name, string? Code, int? BranchNumber, string? PhysicalAddress, string? County,
        string? PhoneNumber, string? Email, string? MpesaShortCode);

    /// <summary>Full replace. The branch number never changes once assigned (it identifies the branch to KRA).</summary>
    public record UpdateBranchRequestDto(string Name, string? Code, string? PhysicalAddress, string? County, string? PhoneNumber, string? Email,
        string? MpesaShortCode);

    /// <param name="DefaultBranchId">The branch signed in to by default; must be one of BranchIds.</param>
    public record AssignUserBranchesRequestDto(IEnumerable<Guid> BranchIds, Guid? DefaultBranchId);

    /// <param name="AccessAll">The user holds Branches.AccessAll: they may work in every branch, whatever is listed.</param>
    public record UserBranchesDto(Guid UserId, bool AccessAll, IEnumerable<BranchDto> Branches, Guid? DefaultBranchId);

    /// <summary>A branch the signed-in user may work in, for the branch switcher.</summary>
    public record BranchMembershipDto(Guid BranchId, int BranchNumber, string KraBranchId, string Name, string? Code, bool IsDefault);
}
