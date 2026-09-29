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
        Task<IEnumerable<StockMovement>> GetStockMovementsAsync(Guid productId);
    }
}
