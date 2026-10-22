using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Domain.Entities
{
    public class Sale : ITenantEntity, IBranchEntity
    {
        public Guid SaleId { get; set; }
        public Guid BusinessId { get; set; }
        /// <summary>Where it was sold; stock left this branch.</summary>
        public Guid BranchId { get; set; }
        public string SaleNumber { get; set; } = string.Empty;
        public Guid? CustomerId { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public SaleStatus Status { get; set; } = SaleStatus.Completed;
        /// <summary>Rider: the sale a delivery order became when the rider came back with the payment.</summary>
        public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.Counter;
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
        /// <summary>MpesaReference sales: the M-Pesa code the cashier typed (10 characters, upper case). Unique per business among sales
        /// that are not voided or failed. Null for other methods.</summary>
        public string? MpesaReference { get; set; }
        /// <summary>Credit sales: the day the customer promised to pay by. An open invoice past it raises an alert. Null = no promise.</summary>
        public DateOnly? PaymentDueDate { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }

        public Customer? Customer { get; set; }
        public Branch Branch { get; set; } = null!;
        public ICollection<SaleLine> Lines { get; set; } = new List<SaleLine>();
        public SalePrescription? Prescription { get; set; }
    }
}
