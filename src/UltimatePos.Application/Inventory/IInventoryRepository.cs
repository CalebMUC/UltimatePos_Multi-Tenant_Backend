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
        Task<IEnumerable<StockLevel>> GetStockLevelsAsync(bool belowReorderOnly);
        Task<StockLevel?> GetStockLevelAsync(Guid productId);
        Task<Dictionary<Guid, decimal>> GetQuantitiesOnHandAsync(IEnumerable<Guid> productIds);
        Task<IEnumerable<StockMovement>> GetStockMovementsAsync(Guid productId);

        Task<Dictionary<Guid, decimal>> GetAverageCostsAsync(IEnumerable<Guid> productIds);

        //Task AdjustStockAsync(Guid productId, decimal quantityChangeInBaseUnits, string reason, Guid? adjustedBy);
        /// <param name="unitCostPerBaseUnit">Cost of the units being ADDED (blended into the average). Null = add at the current
        /// average, which leaves it unchanged. Ignored for removals, which are always valued at the current average.</param>
        Task AdjustStockAsync(Guid productId, decimal quantityChangeInBaseUnits, string reason, Guid? adjustedBy, decimal? unitCostPerBaseUnit);

        /// <summary>Changes the average cost without changing quantity, and records it in the movement ledger.</summary>
        Task RevalueStockAsync(Guid productId, decimal newUnitCostPerBaseUnit, string reason, Guid? revaluedBy);
    }
}
