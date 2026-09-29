using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Inventory;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    public class InventoryRepository : IInventoryRepository
    {
        private readonly UltimatePosDbContext _context;
        public InventoryRepository(UltimatePosDbContext context) => _context = context;

        public async Task<IEnumerable<StockLevel>> GetStockLevelsAsync(bool belowReorderOnly)
        {
            var query = _context.StockLevels.AsNoTracking().Include(s => s.Product).AsQueryable();
            if (belowReorderOnly)
                query = query.Where(s => s.QuantityOnHand <= s.Product.ReorderLevel);
            return await query.ToListAsync();
        }

        public async Task<StockLevel?> GetStockLevelAsync(Guid productId) =>
            await _context.StockLevels.AsNoTracking().Include(s => s.Product).FirstOrDefaultAsync(s => s.ProductId == productId);

        public async Task<IEnumerable<StockMovement>> GetStockMovementsAsync(Guid productId) =>
            await _context.StockMovements.AsNoTracking()
                .Where(m => m.ProductId == productId)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();
    }
}
