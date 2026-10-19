using System;

namespace UltimatePos.Domain.Entities
{
    public enum StockTransferStatus { Draft, Dispatched, Received, Cancelled }

    /// <summary>
    /// Stock moving between two branches of a business. Draft (nothing moves) → Dispatched (stock leaves the source at its average
    /// cost and is in transit) → Received (stock enters the destination at that cost; what didn't arrive is recorded as a shortage).
    /// The stock's value moves with it, so the business's total stock value only changes by a shortage.
    /// </summary>
    public class StockTransfer : ITenantEntity
    {
        public Guid StockTransferId { get; set; }
        public Guid BusinessId { get; set; }
        /// <summary>TRF-000001 ..., per business.</summary>
        public string TransferNumber { get; set; } = string.Empty;
        public Guid FromBranchId { get; set; }
        public Guid ToBranchId { get; set; }
        public StockTransferStatus Status { get; set; } = StockTransferStatus.Draft;
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? DispatchedAt { get; set; }
        public Guid? DispatchedBy { get; set; }
        public DateTime? ReceivedAt { get; set; }
        public Guid? ReceivedBy { get; set; }
        public string? ReceiveNotes { get; set; }
        public DateTime? CancelledAt { get; set; }
        public Guid? CancelledBy { get; set; }

        public Branch FromBranch { get; set; } = null!;
        public Branch ToBranch { get; set; } = null!;
        public ICollection<StockTransferLine> Lines { get; set; } = new List<StockTransferLine>();
    }

    public class StockTransferLine
    {
        public Guid StockTransferLineId { get; set; }
        public Guid StockTransferId { get; set; }
        public Guid ProductId { get; set; }
        /// <summary>The unit the quantity was asked for in.</summary>
        public Guid UnitOfMeasureId { get; set; }
        public decimal Quantity { get; set; }
        public decimal QuantityInBaseUnits { get; set; }
        /// <summary>What actually arrived, in base units. Set on receipt.</summary>
        public decimal? QuantityReceivedInBaseUnits { get; set; }
        /// <summary>Average cost per base unit at the source when dispatched. The destination receives at this cost.</summary>
        public decimal? UnitCost { get; set; }

        public StockTransfer StockTransfer { get; set; } = null!;
        public Product Product { get; set; } = null!;
        public UnitOfMeasure UnitOfMeasure { get; set; } = null!;
        public ICollection<StockTransferLineBatch> Batches { get; set; } = new List<StockTransferLineBatch>();
    }

    /// <summary>For batch-tracked products: which lots left the source, so the same lots (number, expiry) arrive at the destination.</summary>
    public class StockTransferLineBatch
    {
        public Guid StockTransferLineBatchId { get; set; }
        public Guid StockTransferLineId { get; set; }
        public Guid SourceStockBatchId { get; set; }
        public string BatchNumber { get; set; } = string.Empty;
        public DateOnly? ExpiryDate { get; set; }
        public DateOnly? ManufactureDate { get; set; }
        public decimal QuantityInBaseUnits { get; set; }
        public decimal? QuantityReceivedInBaseUnits { get; set; }

        public StockTransferLine Line { get; set; } = null!;
    }
}
