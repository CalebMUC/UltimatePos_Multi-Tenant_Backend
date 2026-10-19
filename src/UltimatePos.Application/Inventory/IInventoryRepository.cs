using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Inventory
{
    public interface IInventoryRepository
    {
        // Lists cover every branch the caller can see (one row per product and branch). Single-product reads, quantities and
        // average costs are AT THE ACTIVE BRANCH: they feed the till, production and adjustments, which happen at one branch.

        Task<IEnumerable<StockLevel>> GetStockLevelsAsync(bool belowReorderOnly);
        Task<StockLevel?> GetStockLevelAsync(Guid productId);
        Task<Dictionary<Guid, decimal>> GetQuantitiesOnHandAsync(IEnumerable<Guid> productIds);
        /// <summary>Sellable quantity per (product, branch), over the branches the caller can see.</summary>
        Task<Dictionary<(Guid ProductId, Guid BranchId), decimal>> GetSellableByBranchAsync(IEnumerable<Guid> productIds, DateOnly today);
        /// <summary>Average cost per (product, branch), over the branches the caller can see.</summary>
        Task<Dictionary<(Guid ProductId, Guid BranchId), decimal>> GetAverageCostsByBranchAsync(IEnumerable<Guid> productIds);
        /// <summary>
        /// One average cost per product over the branches the caller can see, weighted by the stock each holds (a branch with no
        /// stock adds nothing; when none holds any, the highest branch average is used so the cost is still known).
        /// </summary>
        Task<Dictionary<Guid, decimal>> GetWeightedAverageCostsAsync(IEnumerable<Guid> productIds);
        /// <summary>
        /// What can actually be sold today: for batch-tracked products the quantity in batches that have not expired by
        /// <paramref name="today"/>, for the rest the quantity on hand. Products with no stock record are absent.
        /// </summary>
        Task<Dictionary<Guid, decimal>> GetSellableQuantitiesAsync(IEnumerable<Guid> productIds, DateOnly today);
        /// <summary>
        /// Base-unit quantity held back for open delivery orders (pending or out for delivery), per (product, branch), over the branches
        /// the caller can see. Products with nothing reserved are absent.
        /// </summary>
        Task<Dictionary<(Guid ProductId, Guid BranchId), decimal>> GetReservedQuantitiesAsync(IEnumerable<Guid> productIds);
        Task<IEnumerable<StockMovement>> GetStockMovementsAsync(Guid productId);
        Task<Dictionary<Guid, decimal>> GetAverageCostsAsync(IEnumerable<Guid> productIds);
        /// <param name="unitCostPerBaseUnit">Cost of the units being ADDED (blended into the average). Null = add at the current
        /// average, which leaves it unchanged. Ignored for removals, which are always valued at the current average.</param>
        Task AdjustStockAsync(Guid productId, decimal quantityChangeInBaseUnits, string reason, Guid? adjustedBy, decimal? unitCostPerBaseUnit,
            BatchTarget? batch = null);
        /// <summary>Changes the average cost without changing quantity, and records it in the movement ledger.</summary>
        Task RevalueStockAsync(Guid productId, decimal newUnitCostPerBaseUnit, string reason, Guid? revaluedBy);

        Task<StockBatch?> GetBatchAsync(Guid stockBatchId);
        Task<IEnumerable<StockBatch>> GetBatchesAsync(Guid? productId, bool includeEmpty);
    }

    /// <summary>Which lot a batch-tracked adjustment applies to: an existing one by id, or a lot number (created if new).</summary>
    public record BatchTarget(Guid? StockBatchId, string? BatchNumber, DateOnly? ExpiryDate, DateOnly? ManufactureDate);
}
