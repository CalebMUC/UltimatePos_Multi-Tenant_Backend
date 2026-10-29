using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Identity.Dtos;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Platform
{
    /// <summary>The person who will run the business. An existing account (matched by email) is attached; otherwise one is created.</summary>
    /// <param name="Password">For a new account. Leave out to have a temporary password generated and returned once.</param>
    public record BusinessOwnerRequestDto(string FullName, string Email, string? PhoneNumber, string? Password = null);

    /// <param name="TemporaryPassword">Only when the account was created without a password: give it to the owner. Shown once.</param>
    public record BusinessOwnerResultDto(Guid UserId, string Email, string FullName, bool Created, string? TemporaryPassword);

    /// <param name="MaxBranches">Open branches allowed. Null = unlimited.</param>
    public record BusinessLicenseDto(Guid BusinessLicenseId, Guid BusinessId, string LicenseNumber, LicenseKind Kind, string PlanName, int? MaxUsers,
        DateOnly StartsOn, DateOnly EndsOn, decimal Amount, string Currency, SettlementMethod? PaymentMethod, string? PaymentReference, string? Notes,
        LicenseState State, DateTime CreatedAt, Guid? CreatedBy, DateTime? CancelledAt, string? CancelReason, int? MaxBranches = null);

    public record RegisterBusinessResultDto(BusinessDto Business, BusinessOwnerResultDto Owner, BusinessLicenseDto? TrialLicense);

    /// <summary>
    /// A new licence period. Give Months (1–36) or EndsOn. StartsOn defaults to the day after the current licence ends (a renewal),
    /// or today when there is none or it has lapsed.
    /// </summary>
    /// <param name="MaxBranches">Open branches allowed (head office included). Leave out for unlimited.</param>
    public record IssueLicenseRequestDto(LicenseKind Kind, string PlanName, int? Months, DateOnly? StartsOn, DateOnly? EndsOn, int? MaxUsers,
        decimal Amount, SettlementMethod? PaymentMethod, string? PaymentReference, string? Notes, int? MaxBranches = null);

    public record ReasonRequestDto(string Reason);
    /// <summary>Reclassifies a business. Only the classification changes: units, expense categories and settings it started with stay.</summary>
    public record ChangeSectorRequestDto(BusinessSector Sector);

    /// <summary>Switches KRA eTIMS reporting on or off for a business.</summary>
    public record ChangeEtimsRequestDto(bool Enabled);

    public record BusinessAccessDto(string Status, bool Allowed, string? Code, string? Message, DateOnly? LicenseEndsOn, int? DaysRemaining);

    public record BusinessSummaryDto(BusinessDto Business, BusinessAccessDto Access, int ActiveUsers, DateTime CreatedAt, int ActiveBranches = 0);

    /// <param name="Revenue">Completed sales in the period, VAT included, less what was later returned against them.</param>
    public record BusinessPerformanceDto(Guid BusinessId, string BusinessName, BusinessKind Kind, BusinessType BusinessType,
        string AccessStatus, DateOnly? LicenseEndsOn, int ActiveUsers, int ActiveProducts, int SaleCount, decimal Revenue, decimal AverageSale,
        DateTime? LastSaleAt, DateTime? LastSignInAt, DateTime CreatedAt, int ActiveBranches = 0, BusinessSector Sector = BusinessSector.General);
    /// <summary>Businesses of one sector in the dashboard period: how many, and what they sold.</summary>
    public record SectorPerformanceDto(BusinessSector Sector, int BusinessCount, int SaleCount, decimal Revenue);

    public record PlatformDashboardDto(DateOnly From, DateOnly To, int BusinessCount, int ActiveCount, int ExpiringSoonCount, int GracePeriodCount,
        int ExpiredCount, int UnlicensedCount, int SuspendedCount, int ClosedCount, int SaleCount, decimal Revenue, decimal LicenseIncome,
        IEnumerable<BusinessPerformanceDto> Businesses, IEnumerable<SectorPerformanceDto>? BySector = null);

    public record BusinessDetailDto(BusinessSummaryDto Summary, IEnumerable<BusinessLicenseDto> Licenses, IEnumerable<UserDto> Members,
        BusinessPerformanceDto Last30Days);

    public record CreateSuperUserRequestDto(string FullName, string Email, string? PhoneNumber, string? Password = null);
    public record CreatedAccountDto(UserDto User, string? TemporaryPassword);
}
