using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Domain.Entities
{
    public class StockLevel : IBranchEntity
    {
        public Guid ProductId { get; set; }
        /// <summary>Stock is held per branch: one row per (product, branch), each with its own average cost.</summary>
        public Guid BranchId { get; set; }
        public decimal QuantityOnHand { get; set; }

        /// <summary>Weighted-average cost per BASE unit. 0 means no cost has been recorded yet. Changed only by StockLedger.</summary>
        public decimal AverageUnitCost { get; set; }
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

        public Product Product { get; set; } = null!;
        public Branch Branch { get; set; } = null!;
    }
}
