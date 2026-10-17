using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Domain.Entities
{
    public class Sale : ITenantEntity
    {
        public Guid SaleId { get; set; }
        public Guid BusinessId { get; set; }
        public string SaleNumber { get; set; } = string.Empty;
        public Guid? CustomerId { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public SaleStatus Status { get; set; } = SaleStatus.Completed;
        /// <summary>Sum of quantity x unit price before discounts. Prices are VAT-inclusive.</summary>
        public decimal Subtotal { get; set; }
        public decimal DiscountTotal { get; set; }
        /// <summary>VAT contained in TotalAmount. Zero when the business is not VAT-registered.</summary>
        public decimal TaxTotal { get; set; }
        /// <summary>What the customer pays: Subtotal - DiscountTotal, VAT included.</summary>
        public decimal TotalAmount { get; set; }
        /// <summary>Running total of goods returned against this sale, VAT included.</summary>
        public decimal ReturnedAmount { get; set; }
        public decimal? AmountTendered { get; set; }
        public decimal? ChangeDue { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }

        public Customer? Customer { get; set; }
        public ICollection<SaleLine> Lines { get; set; } = new List<SaleLine>();
        public SalePrescription? Prescription { get; set; }
    }
}
