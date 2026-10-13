using System;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Domain.Entities
{
    /// <summary>A non-inventory cost (labour, utilities, ...) charged to one production run.</summary>
    public class ProductionRunCost
    {
        public Guid ProductionRunCostId { get; set; }
        public Guid ProductionRunId { get; set; }
        public ProductionCostType CostType { get; set; }
        public string? Description { get; set; }
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }

        public ProductionRun ProductionRun { get; set; } = null!;
    }
}