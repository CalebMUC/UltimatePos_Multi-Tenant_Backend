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
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }

        public ICollection<Customer> Customers { get; set; } = new List<Customer>();
    }
}
