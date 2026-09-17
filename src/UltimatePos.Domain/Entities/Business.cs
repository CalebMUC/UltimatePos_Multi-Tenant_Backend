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

    public class Business
    {
        public Guid BusinessId { get; set; }
        public string BusinessName { get; set; } = string.Empty;
        public string? TradingName { get; set; }
        public BusinessType BusinessType { get; set; }
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
