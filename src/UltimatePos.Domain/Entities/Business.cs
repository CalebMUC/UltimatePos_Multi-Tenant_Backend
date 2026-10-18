using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Domain.Entities
{
    public enum BusinessType
    {
        Wholesaler,
        Retailer
    }

    /// <summary>
    /// How a business ACQUIRES stock — separate from BusinessType, which is who it SELLS to.
    /// Drives what it may purchase (see PurchasePolicy). A pharmacy or supermarket is a Trader.
    /// </summary>
    public enum BusinessKind
    {
        /// <summary>Buys raw materials/packaging and produces finished goods from formulas (e.g. manufacturing agrovet).</summary>
        Manufacturer,
        /// <summary>Buys finished goods from suppliers and resells them (pharmacy, supermarket, retail shop).</summary>
        Trader
    }

    public class BusinessProfile
    {
        public Guid BusinessId { get; set; }
        public string BusinessName { get; set; } = string.Empty;
        public string? TradingName { get; set; }
        public BusinessType BusinessType { get; set; }
        public BusinessKind Kind { get; set; }
        public string? RegistrationNumber { get; set; }
        public string KraPin { get; set; } = string.Empty;
        public string? PhysicalAddress { get; set; }
        public string? County { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? LogoUrl { get; set; }
        public string? BackgroundImageUrl { get; set; }
        /// <summary>VAT-registered businesses show VAT on every sale line; others charge none.</summary>
        public bool IsVatRegistered { get; set; }
        /// <summary>M-Pesa till or paybill number. Routes incoming till payments to this business.</summary>
        public string? MpesaShortCode { get; set; }
        /// <summary>Largest discount a cashier may give on one sale, as a percentage of its subtotal. Null = no cap.</summary>
        public decimal? MaxDiscountPercent { get; set; }
        /// <summary>False = closed by a super user: nobody can sign in to it. Data is kept.</summary>
        public bool IsActive { get; set; } = true;
        /// <summary>Locked by a super user (typically for an unpaid licence). Reversible; users are refused until it is lifted.</summary>
        public bool IsSuspended { get; set; }
        public DateTime? SuspendedAt { get; set; }
        public Guid? SuspendedBy { get; set; }
        public string? SuspensionReason { get; set; }
        /// <summary>Last day covered by an active licence (kept in step with BusinessLicenses). Null = never licensed.</summary>
        public DateOnly? LicenseEndsOn { get; set; }
        /// <summary>User limit of the licence covering today. Null = unlimited.</summary>
        public int? LicenseMaxUsers { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }

        public ICollection<Customer> Customers { get; set; } = new List<Customer>();
    }
}
