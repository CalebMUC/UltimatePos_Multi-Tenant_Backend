using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Inventory;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    public class InventoryRepository : IInventoryRepository
    {
        private readonly UltimatePosDbContext _context;
        private readonly StockLedger _stockLedger;
        private readonly ITenantContext _tenant;

        public InventoryRepository(UltimatePosDbContext context, StockLedger stockLedger, ITenantContext tenant)
        {
            _context = context;
            _stockLedger = stockLedger;
            _tenant = tenant;
        }

        public async Task<IEnumerable<StockLevel>> GetStockLevelsAsync(bool belowReorderOnly)
        {
            var query = _context.StockLevels.AsNoTracking().Include(s => s.Product).ThenInclude(p => p.BaseUnitOfMeasure).AsQueryable();
            if (belowReorderOnly)
                query = query.Where(s => s.QuantityOnHand <= s.Product.ReorderLevel);
            return await query.ToListAsync();
        }

        public async Task<StockLevel?> GetStockLevelAsync(Guid productId) =>
            await _context.StockLevels.AsNoTracking().Include(s => s.Product).ThenInclude(p => p.BaseUnitOfMeasure)
                .FirstOrDefaultAsync(s => s.ProductId == productId);

        public async Task<Dictionary<Guid, decimal>> GetQuantitiesOnHandAsync(IEnumerable<Guid> productIds)
        {
            var ids = productIds.Distinct().ToList();
            return await _context.StockLevels.AsNoTracking()
                .Where(s => ids.Contains(s.ProductId))
                .ToDictionaryAsync(s => s.ProductId, s => s.QuantityOnHand);
        }

        public async Task<Dictionary<Guid, decimal>> GetSellableQuantitiesAsync(IEnumerable<Guid> productIds, DateOnly today)
        {
            var ids = productIds.Distinct().ToList();
            if (ids.Count == 0)
                return new Dictionary<Guid, decimal>();

            var levels = await _context.StockLevels.AsNoTracking()
                .Where(s => ids.Contains(s.ProductId))
                .Select(s => new { s.ProductId, s.QuantityOnHand, s.Product.IsBatchTracked })
                .ToListAsync();

            var batchTrackedIds = levels.Where(l => l.IsBatchTracked).Select(l => l.ProductId).ToList();
            var inLots = batchTrackedIds.Count == 0
                ? new Dictionary<Guid, decimal>()
                : await _context.StockBatches.AsNoTracking()
                    .Where(b => batchTrackedIds.Contains(b.ProductId) && b.QuantityOnHand > 0 && (b.ExpiryDate == null || b.ExpiryDate >= today))
                    .GroupBy(b => b.ProductId)
                    .Select(g => new { g.Key, Quantity = g.Sum(b => b.QuantityOnHand) })
                    .ToDictionaryAsync(g => g.Key, g => g.Quantity);

            return levels.ToDictionary(l => l.ProductId, l => l.IsBatchTracked ? inLots.GetValueOrDefault(l.ProductId) : l.QuantityOnHand);
        }

        public async Task<StockBatch?> GetBatchAsync(Guid stockBatchId) =>
            await _context.StockBatches.AsNoTracking().FirstOrDefaultAsync(b => b.StockBatchId == stockBatchId);

        public async Task<IEnumerable<StockBatch>> GetBatchesAsync(Guid? productId, bool includeEmpty)
        {
            var query = _context.StockBatches.AsNoTracking().Include(b => b.Product).ThenInclude(p => p.BaseUnitOfMeasure).AsQueryable();
            if (productId.HasValue)
                query = query.Where(b => b.ProductId == productId.Value);
            if (!includeEmpty)
                query = query.Where(b => b.QuantityOnHand > 0);
            return await query.ToListAsync();
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

        public async Task AdjustStockAsync(Guid productId, decimal quantityChangeInBaseUnits, string reason, Guid? adjustedBy, decimal? unitCostPerBaseUnit,
            BatchTarget? batch = null)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            var adjustmentId = Guid.NewGuid();
            var movement = new StockMovement
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
            };

            if (batch?.StockBatchId is { } batchId)
                await _stockLedger.ApplyToBatchAsync(movement, batchId);
            else if (batch?.BatchNumber is { } batchNumber)
                await _stockLedger.ReceiveIntoBatchAsync(movement, _tenant.RequireBusinessId(), batchNumber, batch.ExpiryDate, batch.ManufactureDate, null, null);
            else
                await _stockLedger.ApplyAsync(movement);

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
