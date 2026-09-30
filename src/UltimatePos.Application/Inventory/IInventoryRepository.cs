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
        Task AdjustStockAsync(Guid productId, decimal quantityChangeInBaseUnits, string reason, Guid? adjustedBy);
    }
}
