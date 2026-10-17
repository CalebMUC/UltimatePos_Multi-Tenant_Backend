using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Domain.Entities
{
    public class Customer : ITenantEntity
    {
        public Guid CustomerId { get; set; }
        public Guid BusinessId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string? KraPin { get; set; }
        public string? ContactPerson { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? PhysicalAddress { get; set; }

        public PriceType PriceType { get; set; } = PriceType.Retail;
        /// <summary>Most the customer may owe on credit sales. Null = no limit.</summary>
        public decimal? CreditLimit { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }

        public BusinessProfile BusinessProfile { get; set; } = null!;
    }
}
