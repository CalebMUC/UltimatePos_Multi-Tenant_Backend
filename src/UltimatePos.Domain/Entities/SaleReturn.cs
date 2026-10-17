using System;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Domain.Entities
{
    /// <summary>
    /// Goods brought back against a completed sale. Immutable once recorded: a return is a document of its own, the original
    /// sale stays Completed and only its ReturnedAmount and each line's QuantityReturned move.
    /// </summary>
    public class SaleReturn : ITenantEntity
    {
        public Guid SaleReturnId { get; set; }
        public Guid BusinessId { get; set; }
        /// <summary>RET-000001 ...</summary>
        public string ReturnNumber { get; set; } = string.Empty;
        public Guid SaleId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public RefundMethod RefundMethod { get; set; }
        /// <summary>M-Pesa code when refunded by M-Pesa.</summary>
        public string? RefundReference { get; set; }
        /// <summary>Amount refunded or credited, VAT included.</summary>
        public decimal TotalAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }

        public Sale Sale { get; set; } = null!;
        public ICollection<SaleReturnLine> Lines { get; set; } = new List<SaleReturnLine>();
    }

    public class SaleReturnLine
    {
        public Guid SaleReturnLineId { get; set; }
        public Guid SaleReturnId { get; set; }
        public Guid SaleLineId { get; set; }
        public Guid ProductId { get; set; }
        /// <summary>In the unit the line was sold in.</summary>
        public decimal Quantity { get; set; }
        public decimal QuantityInBaseUnits { get; set; }
        /// <summary>The share of the sale line's charged total being refunded, VAT included.</summary>
        public decimal Amount { get; set; }
        public decimal TaxAmount { get; set; }
        /// <summary>The cost per base unit the goods originally left stock at. Comes off cost of goods sold when restocked.</summary>
        public decimal? UnitCost { get; set; }
        /// <summary>False for damaged, opened or expired goods that are refunded but cannot go back on the shelf.</summary>
        public bool Restock { get; set; }

        public SaleReturn SaleReturn { get; set; } = null!;
        public SaleLine SaleLine { get; set; } = null!;
        public Product Product { get; set; } = null!;
    }
}
