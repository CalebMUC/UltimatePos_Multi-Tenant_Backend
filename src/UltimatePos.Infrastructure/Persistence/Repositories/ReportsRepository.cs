using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UltimatePos.Application.Reports;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    // Read-only queries for the reports. Rows are projected to the narrow shapes ReportsService needs; all grouping and
    // arithmetic happens there so it stays unit-testable.
    public class ReportsRepository : IReportsRepository
    {
        private readonly UltimatePosDbContext _context;
        public ReportsRepository(UltimatePosDbContext context) => _context = context;

        // ---------------------------------------------------------------- Sales

        public async Task<IReadOnlyList<SaleReportRow>> GetSalesAsync(DateTime fromUtc, DateTime toUtcExclusive) =>
            await _context.Sales.AsNoTracking()
                .Where(s => s.Status != SaleStatus.Failed && s.CreatedAt >= fromUtc && s.CreatedAt < toUtcExclusive)
                .OrderBy(s => s.CreatedAt)
                .Select(s => new SaleReportRow(s.SaleId, s.SaleNumber, s.CreatedAt, s.Status, s.PaymentMethod, s.CustomerId,
                    s.Customer != null ? s.Customer.CustomerName : null, s.TotalAmount, s.CreatedBy,
                    _context.Users.Where(u => u.UserId == s.CreatedBy).Select(u => u.Username).FirstOrDefault()))
                .ToListAsync();

        // ---------------------------------------------------------------- Customers

        public async Task<IReadOnlyList<CustomerBalanceRow>> GetCustomerBalancesAsync() =>
            await _context.CustomerBalances.AsNoTracking()
                .Where(b => b.OutstandingBalance != 0)
                .Select(b => new CustomerBalanceRow(b.CustomerId, b.Customer.CustomerName, b.OutstandingBalance))
                .ToListAsync();

        public async Task<IReadOnlyList<CustomerLedgerRow>> GetCustomerLedgerAsync(DateTime toUtcExclusive, Guid? customerId)
        {
            var query = _context.CustomerLedgerEntries.AsNoTracking()
                .Where(e => e.CreatedAt < toUtcExclusive);

            if (customerId.HasValue)
                query = query.Where(e => e.CustomerId == customerId.Value);

            return await query
                .OrderBy(e => e.CustomerId).ThenBy(e => e.CreatedAt)
                .Select(e => new CustomerLedgerRow(e.CustomerId, e.Customer.CustomerName, e.Customer.PhoneNumber, e.EntryType,
                    e.Amount, e.ReferenceType, e.ReferenceId, e.Notes, e.CreatedAt))
                .ToListAsync();
        }

        public async Task<Dictionary<Guid, (string SaleNumber, DateOnly? PaymentDueDate)>> GetInvoiceReferencesAsync(IEnumerable<Guid> saleIds)
        {
            var ids = saleIds.Distinct().ToList();
            if (ids.Count == 0)
                return new Dictionary<Guid, (string, DateOnly?)>();

            // ToDictionaryAsync's selectors run in memory, so project first or EF reads every column of the row.
            return await _context.Sales.AsNoTracking()
                .Where(s => ids.Contains(s.SaleId))
                .Select(s => new { s.SaleId, s.SaleNumber, s.PaymentDueDate })
                .ToDictionaryAsync(s => s.SaleId, s => (s.SaleNumber, s.PaymentDueDate));
        }

        // ---------------------------------------------------------------- Production

        public async Task<IReadOnlyList<ProductionRunReportRow>> GetProductionRunsAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? productId)
        {
            var query = _context.ProductionRuns.AsNoTracking()
                .Where(r => r.CreatedAt >= fromUtc && r.CreatedAt < toUtcExclusive);

            if (productId.HasValue)
                query = query.Where(r => r.ProductId == productId.Value);

            return await query
                .OrderBy(r => r.CreatedAt)
                .Select(r => new ProductionRunReportRow(r.ProductionRunId, r.RunNumber, r.CreatedAt, r.ProductId, r.Product.Name, r.Product.Sku,
                    r.UnitOfMeasureId, r.UnitOfMeasure.Symbol, r.QuantityPlanned, r.QuantityProduced,
                    r.MaterialCost, r.AdditionalCost, r.TotalCost))
                .ToListAsync();
        }

        public async Task<IReadOnlyList<ProductionCostLineRow>> GetProductionCostLinesAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? productId)
        {
            var query = _context.ProductionRunCosts.AsNoTracking()
                .Where(c => c.ProductionRun.CreatedAt >= fromUtc && c.ProductionRun.CreatedAt < toUtcExclusive);

            if (productId.HasValue)
                query = query.Where(c => c.ProductionRun.ProductId == productId.Value);

            return await query
                .Select(c => new ProductionCostLineRow(c.ProductionRunId, c.CostType, c.Amount))
                .ToListAsync();
        }

        public async Task<IReadOnlyList<ProductionInputRow>> GetProductionInputsAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? productId)
        {
            var query = _context.ProductionRunInputs.AsNoTracking()
                .Where(i => i.ProductionRun.CreatedAt >= fromUtc && i.ProductionRun.CreatedAt < toUtcExclusive);

            if (productId.HasValue)
                query = query.Where(i => i.ProductionRun.ProductId == productId.Value);

            return await query
                .Select(i => new ProductionInputRow(i.IngredientProductId, i.IngredientProduct.Name, i.IngredientProduct.Sku,
                    i.IngredientProduct.BaseUnitOfMeasure.Symbol, i.QuantityConsumedInBaseUnits, i.TotalCost))
                .ToListAsync();
        }

        // ---------------------------------------------------------------- Stock

        public async Task<StockAlertCounts> GetStockAlertCountsAsync()
        {
            var active = _context.StockLevels.AsNoTracking().Where(s => s.Product.IsActive);

            // A reorder level of 0 means "never set", so it is not a low-stock signal on its own. Out of stock is counted separately
            // and not as low as well, matching StockStatusRules.
            var low = await active.CountAsync(s => s.QuantityOnHand > 0 && s.Product.ReorderLevel > 0 && s.QuantityOnHand <= s.Product.ReorderLevel);
            var outOfStock = await active.CountAsync(s => s.QuantityOnHand <= 0);
            return new StockAlertCounts(low, outOfStock);
        }

        // ---------------------------------------------------------------- Inventory

        private static readonly StockMovementType[] UsageTypes =
            { StockMovementType.Sale, StockMovementType.SaleVoid, StockMovementType.ProductionConsumption };

        public async Task<IReadOnlyList<StockStatusRow>> GetStockStatusRowsAsync(ItemType? itemType, Guid? categoryId, bool includeInactive)
        {
            var query = _context.StockLevels.AsNoTracking().AsQueryable();

            if (!includeInactive)
                query = query.Where(s => s.Product.IsActive);
            if (itemType.HasValue)
                query = query.Where(s => s.Product.ItemType == itemType.Value);
            if (categoryId.HasValue)
                query = query.Where(s => s.Product.CategoryId == categoryId.Value);

            var rows = await query
                .Select(s => new StockStatusRow(s.ProductId, s.Product.Name, s.Product.Sku, s.Product.ItemType, s.Product.Category.Name,
                    s.Product.BaseUnitOfMeasure.Symbol, s.QuantityOnHand, s.Product.ReorderLevel, s.AverageUnitCost))
                .ToListAsync();

            // One row per product over the branches in view (narrow with ?branchId= for one branch). The average is weighted by
            // what each branch holds, so quantity x average is still the stock's value.
            return rows.GroupBy(r => r.ProductId)
                .Select(g =>
                {
                    var first = g.First();
                    var quantity = g.Sum(r => r.QuantityOnHand);
                    var value = g.Sum(r => r.QuantityOnHand * r.AverageUnitCost);
                    var average = quantity != 0 ? Math.Round(value / quantity, 6, MidpointRounding.AwayFromZero) : g.Max(r => r.AverageUnitCost);
                    return first with { QuantityOnHand = quantity, AverageUnitCost = average };
                })
                .ToList();
        }

        public async Task<IReadOnlyList<ProductUsageRow>> GetStockUsageAsync(DateTime fromUtc, DateTime toUtcExclusive)
        {
            var net = await _context.StockMovements.AsNoTracking()
                .Where(m => m.CreatedAt >= fromUtc && m.CreatedAt < toUtcExclusive && UsageTypes.Contains(m.MovementType))
                .GroupBy(m => m.ProductId)
                .Select(g => new { ProductId = g.Key, Net = g.Sum(m => m.QuantityChange) })
                .ToListAsync();

            // Sales and consumption are negative movements and a void is positive, so the net change negated is the quantity used.
            return net.Select(n => new ProductUsageRow(n.ProductId, -n.Net)).ToList();
        }

        public async Task<IReadOnlyList<StockMovementTotalRow>> GetStockMovementTotalsAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? productId)
        {
            // Revaluations move no quantity, so they are not stock movements for this report.
            var query = _context.StockMovements.AsNoTracking()
                .Where(m => m.CreatedAt >= fromUtc && m.CreatedAt < toUtcExclusive && m.MovementType != StockMovementType.Revaluation);

            if (productId.HasValue)
                query = query.Where(m => m.ProductId == productId.Value);

            var totals = await query
                .GroupBy(m => new { m.ProductId, m.MovementType, IsIncrease = m.QuantityChange > 0 })
                .Select(g => new
                {
                    g.Key.ProductId,
                    g.Key.MovementType,
                    g.Key.IsIncrease,
                    Quantity = g.Sum(m => m.QuantityChange),
                    Value = g.Sum(m => m.QuantityChange * (m.UnitCost ?? 0m)),
                    Count = g.Count()
                })
                .ToListAsync();

            if (totals.Count == 0)
                return new List<StockMovementTotalRow>();

            // Names in a second query keeps the grouped query to plain columns.
            var ids = totals.Select(t => t.ProductId).Distinct().ToList();
            var products = await _context.Products.AsNoTracking()
                .Where(p => ids.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.Name, p.Sku, Symbol = p.BaseUnitOfMeasure.Symbol })
                .ToDictionaryAsync(p => p.ProductId);

            return totals
                .Select(t => new StockMovementTotalRow(t.ProductId, products[t.ProductId].Name, products[t.ProductId].Sku,
                    products[t.ProductId].Symbol, t.MovementType, t.IsIncrease, t.Quantity, t.Value, t.Count))
                .ToList();
        }

        public async Task<Dictionary<Guid, decimal>> GetOpeningStockBalancesAsync(DateTime beforeUtc, Guid? productId)
        {
            var query = _context.StockMovements.AsNoTracking().Where(m => m.CreatedAt < beforeUtc);

            if (productId.HasValue)
                query = query.Where(m => m.ProductId == productId.Value);

            return await query
                .GroupBy(m => m.ProductId)
                .Select(g => new { ProductId = g.Key, Quantity = g.Sum(m => m.QuantityChange) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Quantity);
        }

        public async Task<IReadOnlyList<StockAdjustmentRow>> GetStockAdjustmentsAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? productId)
        {
            var query = _context.StockMovements.AsNoTracking()
                .Where(m => m.MovementType == StockMovementType.Adjustment && m.CreatedAt >= fromUtc && m.CreatedAt < toUtcExclusive);

            if (productId.HasValue)
                query = query.Where(m => m.ProductId == productId.Value);

            return await query
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => new StockAdjustmentRow(m.StockMovementId, m.CreatedAt, m.ProductId, m.Product.Name, m.Product.Sku,
                    m.Product.BaseUnitOfMeasure.Symbol, m.QuantityChange, m.UnitCost, m.Notes, m.CreatedBy))
                .ToListAsync();
        }

        public async Task<Dictionary<Guid, string>> GetUserNamesAsync(IEnumerable<Guid> userIds)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0)
                return new Dictionary<Guid, string>();

            // Project first: without it EF reads the whole user row, password hash and salt included, just for the name.
            return await _context.Users.AsNoTracking()
                .Where(u => ids.Contains(u.UserId))
                .Select(u => new { u.UserId, u.Username })
                .ToDictionaryAsync(u => u.UserId, u => u.Username);
        }

        // ---------------------------------------------------------------- Purchasing

        public async Task<IReadOnlyList<PurchaseLineRow>> GetPurchaseLinesAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? supplierId, Guid? productId)
        {
            var query = ActivePurchaseLines()
                .Where(l => l.PurchaseOrder.OrderDate >= fromUtc && l.PurchaseOrder.OrderDate < toUtcExclusive);

            if (supplierId.HasValue)
                query = query.Where(l => l.PurchaseOrder.SupplierId == supplierId.Value);
            if (productId.HasValue)
                query = query.Where(l => l.ProductId == productId.Value);

            return await ProjectPurchaseLines(query.OrderBy(l => l.PurchaseOrder.OrderDate).ThenBy(l => l.PurchaseOrder.OrderNumber)).ToListAsync();
        }

        public async Task<IReadOnlyList<PurchaseLineRow>> GetOpenPurchaseLinesAsync(Guid? supplierId)
        {
            var query = ActivePurchaseLines()
                .Where(l => l.PurchaseOrder.Status == PurchaseOrderStatus.Ordered || l.PurchaseOrder.Status == PurchaseOrderStatus.PartiallyReceived);

            if (supplierId.HasValue)
                query = query.Where(l => l.PurchaseOrder.SupplierId == supplierId.Value);

            return await ProjectPurchaseLines(query.OrderBy(l => l.PurchaseOrder.OrderDate).ThenBy(l => l.PurchaseOrder.OrderNumber)).ToListAsync();
        }

        private IQueryable<PurchaseOrderLine> ActivePurchaseLines() =>
            _context.PurchaseOrderLines.AsNoTracking().Where(l => l.IsActive && l.PurchaseOrder.IsActive);

        private static IQueryable<PurchaseLineRow> ProjectPurchaseLines(IQueryable<PurchaseOrderLine> lines) =>
            lines.Select(l => new PurchaseLineRow(l.PurchaseOrderId, l.PurchaseOrder.OrderNumber, l.PurchaseOrder.OrderDate,
                l.PurchaseOrder.ExpectedDeliveryDate, l.PurchaseOrder.Status, l.PurchaseOrder.SupplierId, l.PurchaseOrder.Supplier.SupplierName,
                l.ProductId, l.Product.Name, l.Product.Sku, l.UnitOfMeasureId, l.UnitOfMeasure.Symbol,
                l.QuantityOrdered, l.QuantityReceived, l.UnitCost));
    }
}
