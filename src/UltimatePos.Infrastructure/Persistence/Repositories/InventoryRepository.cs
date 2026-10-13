using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Inventory;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    public class InventoryRepository : IInventoryRepository
    {
        private readonly UltimatePosDbContext _context;
        private readonly StockLedger _stockLedger;

        public InventoryRepository(UltimatePosDbContext context, StockLedger stockLedger)
        {
            _context = context;
            _stockLedger = stockLedger;
        }

        public async Task<IEnumerable<StockLevel>> GetStockLevelsAsync(bool belowReorderOnly)
        {
            var query = _context.StockLevels.AsNoTracking().Include(s => s.Product).AsQueryable();
            if (belowReorderOnly)
                query = query.Where(s => s.QuantityOnHand <= s.Product.ReorderLevel);
            return await query.ToListAsync();
        }

        public async Task<StockLevel?> GetStockLevelAsync(Guid productId) =>
            await _context.StockLevels.AsNoTracking().Include(s => s.Product).FirstOrDefaultAsync(s => s.ProductId == productId);

        public async Task<Dictionary<Guid, decimal>> GetQuantitiesOnHandAsync(IEnumerable<Guid> productIds)
        {
            var ids = productIds.Distinct().ToList();
            return await _context.StockLevels.AsNoTracking()
                .Where(s => ids.Contains(s.ProductId))
                .ToDictionaryAsync(s => s.ProductId, s => s.QuantityOnHand);
        }

        public async Task<IEnumerable<StockMovement>> GetStockMovementsAsync(Guid productId) =>
            await _context.StockMovements.AsNoTracking()
                .Where(m => m.ProductId == productId)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();

        public async Task<Dictionary<Guid, decimal>> GetAverageCostsAsync(IEnumerable<Guid> productIds)
        {
            var ids = productIds.Distinct().ToList();
            return await _context.StockLevels.AsNoTracking()
                .Where(s => ids.Contains(s.ProductId))
                .ToDictionaryAsync(s => s.ProductId, s => s.AverageUnitCost);
        }

        public async Task AdjustStockAsync(Guid productId, decimal quantityChangeInBaseUnits, string reason, Guid? adjustedBy, decimal? unitCostPerBaseUnit)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            var adjustmentId = Guid.NewGuid();
            await _stockLedger.ApplyAsync(new StockMovement
            {
                ProductId = productId,
                MovementType = StockMovementType.Adjustment,
                QuantityChange = quantityChangeInBaseUnits,
                UnitCost = unitCostPerBaseUnit, // only read for additions; removals are valued by the ledger at the current average
                ReferenceType = "StockAdjustment",
                ReferenceId = adjustmentId,
                BatchId = adjustmentId,
                Notes = reason,
                CreatedBy = adjustedBy
            });

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }

        public async Task RevalueStockAsync(Guid productId, decimal newUnitCostPerBaseUnit, string reason, Guid? revaluedBy)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            var previous = await _stockLedger.RevalueAsync(productId, newUnitCostPerBaseUnit);

            // Append-only audit trail: a revaluation moves no quantity, but it changes what the stock is worth.
            var revaluationId = Guid.NewGuid();
            _context.StockMovements.Add(new StockMovement
            {
                ProductId = productId,
                MovementType = StockMovementType.Revaluation,
                QuantityChange = 0,
                UnitCost = newUnitCostPerBaseUnit,
                ReferenceType = "StockRevaluation",
                ReferenceId = revaluationId,
                BatchId = revaluationId,
                Notes = $"{reason} (average cost {previous:0.######} -> {newUnitCostPerBaseUnit:0.######})",
                CreatedBy = revaluedBy
            });

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }
    }
}
