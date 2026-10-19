using System;

namespace UltimatePos.Domain.Entities
{
    /// <summary>
    /// One received lot of a batch-tracked product. The product's StockLevel stays the total across its batches; the batch
    /// rows say which lots that total is made of, so stock can be issued first-expiry-first-out and expired lots blocked.
    /// QuantityOnHand is changed only by StockLedger, like StockLevel.
    /// </summary>
    public class StockBatch : ITenantEntity, IBranchEntity
    {
        public Guid StockBatchId { get; set; }
        public Guid BusinessId { get; set; }
        public Guid ProductId { get; set; }
        /// <summary>The branch holding the lot. A lot transferred elsewhere becomes a lot of the same number there.</summary>
        public Guid BranchId { get; set; }
        /// <summary>The lot number printed on the pack. Unique per product within the business.</summary>
        public string BatchNumber { get; set; } = string.Empty;
        /// <summary>Null for a batch-tracked product that has no expiry (rare; such a batch is issued after every dated batch).</summary>
        public DateOnly? ExpiryDate { get; set; }
        public DateOnly? ManufactureDate { get; set; }
        /// <summary>In the product's base unit.</summary>
        public decimal QuantityOnHand { get; set; }
        /// <summary>Cost per base unit this lot came in at. Informational: stock is valued at the product's average cost.</summary>
        public decimal? UnitCost { get; set; }
        public Guid? SupplierId { get; set; }
        public Guid? PurchaseOrderId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

        public Product Product { get; set; } = null!;
        public Branch Branch { get; set; } = null!;
    }
}
