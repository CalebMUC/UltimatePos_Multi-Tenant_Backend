using System;

namespace UltimatePos.Domain.Entities
{
    /// <summary>
    /// Which batches a sale line was filled from. Lets a void or return put stock back into the lots it actually left,
    /// and answers recall questions ("who bought batch X?"). Immutable apart from QuantityReturnedInBaseUnits.
    /// </summary>
    public class SaleLineBatchAllocation
    {
        public Guid SaleLineBatchAllocationId { get; set; }
        public Guid SaleLineId { get; set; }
        public Guid StockBatchId { get; set; }
        public decimal QuantityInBaseUnits { get; set; }
        public decimal QuantityReturnedInBaseUnits { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public SaleLine SaleLine { get; set; } = null!;
        public StockBatch StockBatch { get; set; } = null!;
    }
}
