using System;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Domain.Entities
{
    public enum LicenseKind { Trial, Paid, Complimentary }
    public enum LicenseState { Active, Cancelled }

    /// <summary>
    /// One paid (or trial) period of a business's right to use the system. Renewing adds a new period after the last one, so the
    /// rows are the business's licence history. Managed by super users only; the business can read its own.
    /// </summary>
    public class BusinessLicense : ITenantEntity
    {
        public Guid BusinessLicenseId { get; set; }
        public Guid BusinessId { get; set; }
        /// <summary>LIC-000001 ..., numbered per business.</summary>
        public string LicenseNumber { get; set; } = string.Empty;
        public LicenseKind Kind { get; set; }
        /// <summary>Free text: "Standard", "Pharmacy Pro"... Pricing lives with the people selling it, not in the system.</summary>
        public string PlanName { get; set; } = string.Empty;
        /// <summary>Most active users the business may have during this period. Null = unlimited.</summary>
        public int? MaxUsers { get; set; }
        /// <summary>Most active branches (head office included) during this period. Null = unlimited.</summary>
        public int? MaxBranches { get; set; }
        /// <summary>First and last day covered, both inclusive.</summary>
        public DateOnly StartsOn { get; set; }
        public DateOnly EndsOn { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "KES";
        public SettlementMethod? PaymentMethod { get; set; }
        public string? PaymentReference { get; set; }
        public string? Notes { get; set; }
        public LicenseState State { get; set; } = LicenseState.Active;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? CancelledAt { get; set; }
        public Guid? CancelledBy { get; set; }
        public string? CancelReason { get; set; }
    }
}
