using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Costing;
using UltimatePos.Application.Production;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    public class ProductionRepository : IProductionRepository
    {
        private readonly UltimatePosDbContext _context;
        private readonly StockLedger _stockLedger;

        public ProductionRepository(UltimatePosDbContext context, StockLedger stockLedger)
        {
            _context = context;
            _stockLedger = stockLedger;
        }

        public async Task<int> GetNextDocumentNumberAsync(string prefix)
        {
            return await _context.NextSequenceNumberAsync(prefix);
        }

        public async Task<Formula> CreateFormulaVersionAsync(Formula formula, Guid? updatedBy)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            // Lock the product row so two simultaneous "new version" requests for one product queue up
            // instead of computing the same next version number.
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "Products" WHERE "ProductId" = {formula.ProductId} FOR UPDATE""");

            var maxVersion = await _context.Formulas
                .Where(f => f.ProductId == formula.ProductId)
                .MaxAsync(f => (int?)f.VersionNumber) ?? 0;
            formula.VersionNumber = maxVersion + 1;

            var previouslyActive = await _context.Formulas
                .Where(f => f.ProductId == formula.ProductId && f.IsActive)
                .ToListAsync();
            foreach (var previous in previouslyActive)
            {
                previous.IsActive = false;
                previous.LastUpdatedBy = updatedBy;
                previous.LastUpdatedAt = DateTime.UtcNow;
            }
            // Retire first, in its own statement batch: the partial unique index allows only ONE active row per
            // product, and EF may otherwise order the INSERT ahead of the UPDATE.
            await _context.SaveChangesAsync();

            _context.Formulas.Add(formula);
            await _context.SaveChangesAsync();

            await tx.CommitAsync();
            return formula;
        }

        public async Task<IEnumerable<Formula>> GetFormulasAsync(Guid? productId, bool activeOnly)
        {
            var query = _context.Formulas.AsNoTracking()
                .Include(f => f.Product)
                .Include(f => f.Lines).ThenInclude(l => l.IngredientProduct)
                .AsQueryable();

            if (productId.HasValue)
                query = query.Where(f => f.ProductId == productId.Value);
            if (activeOnly)
                query = query.Where(f => f.IsActive);

            return await query.OrderBy(f => f.Product.Name).ThenByDescending(f => f.VersionNumber).ToListAsync();
        }

        public async Task<Formula?> GetFormulaByIdAsync(Guid formulaId) =>
            await _context.Formulas.AsNoTracking()
                .Include(f => f.Product)
                .Include(f => f.Lines).ThenInclude(l => l.IngredientProduct)
                .FirstOrDefaultAsync(f => f.FormulaId == formulaId);

        public async Task DeactivateFormulaAsync(Guid formulaId, Guid? updatedBy)
        {
            var formula = await _context.Formulas.FirstOrDefaultAsync(f => f.FormulaId == formulaId)
                ?? throw new NotFoundException($"Formula '{formulaId}' not found.");

            formula.IsActive = false;
            formula.LastUpdatedBy = updatedBy;
            formula.LastUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
        }

        public async Task RecordProductionRunAsync(ProductionRun run, IEnumerable<StockMovement> movements)
        {
            var all = movements.ToList();
            var consumption = all.Where(m => m.MovementType == StockMovementType.ProductionConsumption).OrderBy(m => m.ProductId).ToList();
            var output = all.Single(m => m.MovementType == StockMovementType.ProductionOutput);

            await using var tx = await _context.Database.BeginTransactionAsync();

            // Lock every stock row this run touches, in product order, before reading any cost. Concurrent runs then
            // queue in the same sequence (no deadlocks) and each sees a stable average cost for its whole transaction.
            await _stockLedger.LockAsync(all.Select(m => m.ProductId));

            _context.ProductionRuns.Add(run);

            // 1. Consume the ingredients. The ledger stamps each movement with the average cost at that instant.
            foreach (var movement in consumption)
                await _stockLedger.ApplyAsync(movement);

            // 2. Price each input, then roll the run up: material + labour/other, divided by what was actually produced.
            foreach (var input in run.Inputs)
            {
                var cost = consumption.Single(m => m.ProductId == input.IngredientProductId).UnitCost ?? 0m;
                input.UnitCost = cost;
                input.TotalCost = CostingCalculator.LineCost(input.QuantityConsumedInBaseUnits, cost);
            }

            var costing = CostingCalculator.ForProduction(
                run.Inputs.Select(i => i.TotalCost), run.AdditionalCosts.Select(c => c.Amount), output.QuantityChange);
            run.MaterialCost = costing.MaterialCost;
            run.AdditionalCost = costing.AdditionalCost;
            run.TotalCost = costing.TotalCost;
            run.UnitCost = costing.UnitCostPerBaseUnit;

            // 3. The finished good enters stock at that cost, blended into its average like any other receipt.
            output.UnitCost = costing.UnitCostPerBaseUnit;
            await _stockLedger.ApplyAsync(output);

            // Run + inputs + cost lines + movements commit together. If any ingredient is short, ApplyAsync throws before
            // this line, the transaction is disposed uncommitted, and nothing — balances included — changes.
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }

        public async Task<(IEnumerable<ProductionRun> Items, int TotalCount)> GetProductionRunsAsync(int page, int pageSize, Guid? productId)
        {
            var query = _context.ProductionRuns.AsNoTracking()
                .Include(r => r.Formula)
                .Include(r => r.Product)
                .Include(r => r.Inputs)
                .AsQueryable();

            if (productId.HasValue)
                query = query.Where(r => r.ProductId == productId.Value);

            var totalCount = await query.CountAsync();
            var items = await query.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return (items, totalCount);
        }

        public async Task<ProductionRun?> GetProductionRunByIdAsync(Guid productionRunId) =>
            await _context.ProductionRuns.AsNoTracking()
                .Include(r => r.Formula)
                .Include(r => r.Product)
                .Include(r => r.Inputs)
                .FirstOrDefaultAsync(r => r.ProductionRunId == productionRunId);
    }
}
