using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UltimatePos.Application.Costing;
using UltimatePos.Application.Reports.Dtos;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Application.Reports.ReportMath;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Reports
{
    /// <summary>
    /// Every business report: sales, production costs, customer ageing, inventory and purchasing. Inventory cost figures are
    /// cost data, so those methods take <c>includeCosts</c> and leave the cost fields null when it is false; the controller sets
    /// it from the caller's Costing.View permission.
    /// </summary>
    public class ReportsService
    {
        public const int DefaultUsageDays = 30;
        public const int MaxUsageDays = 366;

        private readonly IReportsRepository _repository;
        private readonly ICostingRepository _costingRepository;

        public ReportsService(IReportsRepository repository, ICostingRepository costingRepository)
        {
            _repository = repository;
            _costingRepository = costingRepository;
        }

        // =====================================================================
        // Sales
        // =====================================================================

        /// <param name="from">First day, inclusive.</param>
        /// <param name="to">Last day, inclusive.</param>
        /// <param name="top">How many customers and products to list (1..50).</param>
        public async Task<SalesReportDto> GetSalesReportAsync(DateOnly from, DateOnly to, int utcOffsetMinutes, int top)
        {
            var period = ReportPeriod.Create(from, to, utcOffsetMinutes);

            var sales = await _repository.GetSalesAsync(period.FromUtc, period.ToUtcExclusive);
            var lines = await _costingRepository.GetSaleCostRowsAsync(period.FromUtc, period.ToUtcExclusive, null);

            return BuildSalesReport(period, sales, lines, ReportLimits.ClampTop(top));
        }

        public static SalesReportDto BuildSalesReport(ReportPeriod period, IReadOnlyList<SaleReportRow> sales,
            IReadOnlyList<SaleCostRow> completedSaleLines, int top)
        {
            var completed = sales.Where(s => s.Status == SaleStatus.Completed).ToList();
            var voided = sales.Where(s => s.Status == SaleStatus.Voided).ToList();
            var pending = sales.Where(s => s.Status == SaleStatus.Pending).ToList();
            var revenue = completed.Sum(s => s.TotalAmount);

            // Every day in the range gets a row, so a chart has no gaps.
            var perDay = completed.GroupBy(s => period.LocalDateOf(s.CreatedAt))
                .ToDictionary(g => g.Key, g => new SalesByDayDto(g.Key, g.Count(), g.Sum(s => s.TotalAmount)));
            var byDay = Enumerable.Range(0, period.DayCount)
                .Select(i => period.From.AddDays(i))
                .Select(d => perDay.TryGetValue(d, out var row) ? row : new SalesByDayDto(d, 0, 0m))
                .ToList();

            var byMethod = completed.GroupBy(s => s.PaymentMethod)
                .Select(g => new SalesByPaymentMethodDto(g.Key.ToString(), g.Count(), g.Sum(s => s.TotalAmount),
                    Percent(g.Sum(s => s.TotalAmount), revenue)))
                .OrderByDescending(m => m.Total).ThenBy(m => m.PaymentMethod)
                .ToList();

            // Walk-in sales (no customer on the sale) are one bucket of their own.
            var topCustomers = completed.GroupBy(s => s.CustomerId)
                .Select(g => new SalesByCustomerDto(g.Key, g.Key.HasValue ? g.First().CustomerName ?? "Unknown customer" : "Walk-in",
                    g.Count(), g.Sum(s => s.TotalAmount)))
                .OrderByDescending(c => c.Total).ThenBy(c => c.CustomerName)
                .Take(top)
                .ToList();

            var lineRevenue = completedSaleLines.Sum(l => l.LineTotal);
            var topProducts = completedSaleLines.GroupBy(l => (l.ProductId, l.ProductName, l.Sku))
                .Select(g => new SalesByProductDto(g.Key.ProductId, g.Key.ProductName, g.Key.Sku,
                    g.Sum(l => l.QuantityInBaseUnits), g.Sum(l => l.LineTotal), Percent(g.Sum(l => l.LineTotal), lineRevenue)))
                .OrderByDescending(p => p.Revenue).ThenBy(p => p.ProductName)
                .Take(top)
                .ToList();

            var topProductUnits = completedSaleLines.GroupBy(l => (l.ProductId, l.UnitOfMeasureId))
                .Select(g => new SalesByProductUnitDto(g.Key.ProductId, g.First().ProductName, g.First().Sku, g.Key.UnitOfMeasureId,
                    g.First().UnitSymbol ?? string.Empty, g.Sum(l => l.Quantity), g.Sum(l => l.QuantityInBaseUnits), g.Sum(l => l.LineTotal),
                    Percent(g.Sum(l => l.LineTotal), lineRevenue)))
                .OrderByDescending(p => p.Revenue).ThenBy(p => p.ProductName).ThenBy(p => p.UnitSymbol)
                .Take(top)
                .ToList();

            var byCashier = completed.GroupBy(s => s.CreatedBy)
                .Select(g => new SalesByCashierDto(g.Key, g.First().CashierName, g.Count(), g.Sum(s => s.TotalAmount),
                    Percent(g.Sum(s => s.TotalAmount), revenue)))
                .OrderByDescending(c => c.Total).ThenBy(c => c.CashierName)
                .ToList();

            return new SalesReportDto(period.From, period.To, completed.Count, revenue, Average(revenue, completed.Count),
                voided.Count, voided.Sum(s => s.TotalAmount), pending.Count, pending.Sum(s => s.TotalAmount),
                byDay, byMethod, topCustomers, topProducts, topProductUnits, byCashier);
        }

        // =====================================================================
        // Production costs
        // =====================================================================

        /// <param name="from">First day, inclusive.</param>
        /// <param name="to">Last day, inclusive.</param>
        /// <param name="top">How many ingredients to list (1..50).</param>
        public async Task<ProductionCostReportDto> GetProductionCostReportAsync(DateOnly from, DateOnly to, int utcOffsetMinutes, Guid? productId, int top)
        {
            var period = ReportPeriod.Create(from, to, utcOffsetMinutes);

            var runs = await _repository.GetProductionRunsAsync(period.FromUtc, period.ToUtcExclusive, productId);
            var costLines = await _repository.GetProductionCostLinesAsync(period.FromUtc, period.ToUtcExclusive, productId);
            var inputs = await _repository.GetProductionInputsAsync(period.FromUtc, period.ToUtcExclusive, productId);

            return BuildProductionCostReport(period, runs, costLines, inputs, ReportLimits.ClampTop(top));
        }

        public static ProductionCostReportDto BuildProductionCostReport(ReportPeriod period, IReadOnlyList<ProductionRunReportRow> runs,
            IReadOnlyList<ProductionCostLineRow> costLines, IReadOnlyList<ProductionInputRow> inputs, int top)
        {
            var byProduct = runs.GroupBy(r => (r.ProductId, r.ProductName, r.Sku, r.UnitOfMeasureId, r.UnitSymbol))
                .Select(g =>
                {
                    var produced = g.Sum(r => r.QuantityProduced);
                    var planned = g.Sum(r => r.QuantityPlanned);
                    var total = g.Sum(r => r.TotalCost);
                    var perRun = g.Select(r => CostPerUnit(r.TotalCost, r.QuantityProduced)).ToList();
                    var latest = g.OrderByDescending(r => r.CreatedAt).First();

                    return new ProductionCostByProductDto(g.Key.ProductId, g.Key.ProductName, g.Key.Sku, g.Key.UnitOfMeasureId, g.Key.UnitSymbol,
                        g.Count(), planned, produced, Percent(produced, planned),
                        g.Sum(r => r.MaterialCost), g.Sum(r => r.AdditionalCost), total,
                        CostPerUnit(total, produced), perRun.Min(), perRun.Max(), CostPerUnit(latest.TotalCost, latest.QuantityProduced), latest.CreatedAt);
                })
                .OrderByDescending(p => p.TotalCost).ThenBy(p => p.ProductName).ThenBy(p => p.UnitSymbol)
                .ToList();

            var additionalTotal = costLines.Sum(c => c.Amount);
            var byType = costLines.GroupBy(c => c.CostType)
                .Select(g => new ProductionCostByTypeDto(g.Key.ToString(), g.Sum(c => c.Amount), Percent(g.Sum(c => c.Amount), additionalTotal)))
                .OrderByDescending(t => t.Amount).ThenBy(t => t.CostType)
                .ToList();

            var materialTotal = inputs.Sum(i => i.TotalCost);
            var topIngredients = inputs.GroupBy(i => (i.IngredientProductId, i.IngredientName, i.IngredientSku, i.BaseUnitSymbol))
                .Select(g => new ProductionIngredientCostDto(g.Key.IngredientProductId, g.Key.IngredientName, g.Key.IngredientSku, g.Key.BaseUnitSymbol,
                    g.Sum(i => i.QuantityInBaseUnits), g.Sum(i => i.TotalCost), Percent(g.Sum(i => i.TotalCost), materialTotal)))
                .OrderByDescending(i => i.TotalCost).ThenBy(i => i.IngredientName)
                .Take(top)
                .ToList();

            return new ProductionCostReportDto(period.From, period.To, runs.Count,
                runs.Sum(r => r.MaterialCost), runs.Sum(r => r.AdditionalCost), runs.Sum(r => r.TotalCost),
                byProduct, byType, topIngredients);
        }

        // =====================================================================
        // Customer ageing
        // =====================================================================

        /// <param name="asOf">The day to age the debt to, default today. Only ledger entries up to the end of that day count.</param>
        /// <param name="customerId">Limit the report to one customer.</param>
        /// <param name="includeInvoices">Also list each customer's open invoices (which ones to chase, and how old).</param>
        /// <param name="includeSettled">Also list customers who owe nothing and are not in credit.</param>
        public async Task<CustomerAgingReportDto> GetCustomerAgingAsync(DateOnly? asOf, int utcOffsetMinutes, Guid? customerId,
            bool includeInvoices, bool includeSettled)
        {
            ReportPeriod.ValidateOffset(utcOffsetMinutes);
            var date = asOf ?? ReportPeriod.TodayAt(utcOffsetMinutes);
            if (date.Year < 2000 || date.Year > 2200)
                throw new InvalidAssignmentException("'asOf' must be between the years 2000 and 2200.");

            var offset = TimeSpan.FromMinutes(utcOffsetMinutes);
            var ledger = await _repository.GetCustomerLedgerAsync(ReportPeriod.StartOfLocalDayUtc(date.AddDays(1), offset), customerId);

            var computed = ledger
                .GroupBy(r => (r.CustomerId, r.CustomerName, r.PhoneNumber))
                .Select(g => (Customer: g.Key, Aging: AgingCalculator.Calculate(g, date, offset)))
                .Where(c => includeSettled || c.Aging.Outstanding != 0 || c.Aging.UnappliedCredit != 0)
                .OrderByDescending(c => c.Aging.Outstanding).ThenBy(c => c.Customer.CustomerName)
                .ToList();

            var saleNumbers = new Dictionary<Guid, string>();
            if (includeInvoices)
                saleNumbers = await _repository.GetSaleNumbersAsync(computed.SelectMany(c => c.Aging.OpenInvoices.Select(i => i.InvoiceId)));

            var rows = computed.Select(c =>
            {
                var a = c.Aging;
                var oldest = a.OpenInvoices.FirstOrDefault();
                return new CustomerAgingRowDto(c.Customer.CustomerId, c.Customer.CustomerName, c.Customer.PhoneNumber,
                    a.Outstanding, a.UnappliedCredit, a.Outstanding - a.UnappliedCredit,
                    new AgingBucketsDto(a.Days0To30, a.Days31To60, a.Days61To90, a.Over90),
                    a.OpenInvoices.Count, oldest?.InvoiceDate, oldest?.AgeDays,
                    includeInvoices
                        ? a.OpenInvoices.Select(i => new OpenInvoiceDto(i.InvoiceId, saleNumbers.GetValueOrDefault(i.InvoiceId),
                            i.InvoiceDate, i.OriginalAmount, i.OutstandingAmount, i.AgeDays, i.Bucket)).ToList()
                        : null);
            }).ToList();

            return new CustomerAgingReportDto(date, rows.Count, rows.Sum(r => r.Outstanding), rows.Sum(r => r.UnappliedCredit),
                new AgingBucketsDto(rows.Sum(r => r.Buckets.Days0To30), rows.Sum(r => r.Buckets.Days31To60),
                    rows.Sum(r => r.Buckets.Days61To90), rows.Sum(r => r.Buckets.Over90)),
                rows);
        }

        // =====================================================================
        // Inventory: stock status
        // =====================================================================

        /// <param name="status">Only list products in this status. The counts and totals still cover every product matching the other filters.</param>
        /// <param name="usageDays">How many complete days before today to measure usage over (1..366).</param>
        public async Task<StockStatusReportDto> GetStockStatusAsync(ItemType? itemType, Guid? categoryId, StockStatus? status,
            bool includeInactive, int usageDays, int utcOffsetMinutes, bool includeCosts)
        {
            if (usageDays < 1 || usageDays > MaxUsageDays)
                throw new InvalidAssignmentException($"'usageDays' must be between 1 and {MaxUsageDays}.");

            var today = ReportPeriod.TodayAt(utcOffsetMinutes); // also validates the offset
            var offset = TimeSpan.FromMinutes(utcOffsetMinutes);
            var usageFrom = today.AddDays(-usageDays);

            var rows = await _repository.GetStockStatusRowsAsync(itemType, categoryId, includeInactive);
            var usage = await _repository.GetStockUsageAsync(
                ReportPeriod.StartOfLocalDayUtc(usageFrom, offset), ReportPeriod.StartOfLocalDayUtc(today, offset));

            return BuildStockStatus(usageFrom, today.AddDays(-1), usageDays, rows, usage, status, includeCosts);
        }

        public static StockStatusReportDto BuildStockStatus(DateOnly usageFrom, DateOnly usageTo, int usageDays,
            IReadOnlyList<StockStatusRow> rows, IReadOnlyList<ProductUsageRow> usage, StockStatus? statusFilter, bool includeCosts)
        {
            var usedByProduct = usage.GroupBy(u => u.ProductId).ToDictionary(g => g.Key, g => g.Sum(u => u.QuantityUsed));

            var all = rows.Select(r =>
            {
                var status = StockStatusRules.Classify(r.QuantityOnHand, r.ReorderLevel);
                var used = Math.Max(0m, usedByProduct.GetValueOrDefault(r.ProductId));
                var perDay = Math.Round(used / usageDays, 4, MidpointRounding.AwayFromZero);
                decimal? cover = used > 0
                    ? Math.Max(0m, Math.Round(r.QuantityOnHand * usageDays / used, 1, MidpointRounding.AwayFromZero))
                    : null;
                var shortfall = status == StockStatus.InStock ? 0m : Math.Max(0m, r.ReorderLevel - r.QuantityOnHand);

                // Same value and missing-cost rules as GET /costing/stock-valuation.
                var value = CostingCalculator.LineCost(r.QuantityOnHand, r.AverageUnitCost);
                var costMissing = r.QuantityOnHand > 0 && r.AverageUnitCost == 0;

                return (Row: r, Status: status, Item: new StockStatusItemDto(r.ProductId, r.ProductName, r.Sku, r.ItemType.ToString(),
                    r.CategoryName, r.BaseUnitSymbol, r.QuantityOnHand, r.ReorderLevel, status.ToString(), shortfall, perDay, cover,
                    includeCosts ? r.AverageUnitCost : null, includeCosts ? value : null, includeCosts ? costMissing : null),
                    Value: value, CostMissing: costMissing);
            }).ToList();

            var byType = all.GroupBy(a => a.Row.ItemType)
                .Select(g => new StockStatusByItemTypeDto(g.Key.ToString(), g.Count(),
                    g.Count(a => a.Status == StockStatus.LowStock), g.Count(a => a.Status == StockStatus.OutOfStock),
                    includeCosts ? g.Sum(a => a.Value) : null))
                .OrderBy(t => t.ItemType)
                .ToList();

            // Most urgent first: out of stock, then low, each by how soon it runs out; products with no recent usage last.
            var items = all
                .Where(a => !statusFilter.HasValue || a.Status == statusFilter.Value)
                .OrderBy(a => a.Status == StockStatus.OutOfStock ? 0 : a.Status == StockStatus.LowStock ? 1 : 2)
                .ThenBy(a => a.Item.DaysOfCover.HasValue ? 0 : 1)
                .ThenBy(a => a.Item.DaysOfCover ?? 0m)
                .ThenBy(a => a.Item.ProductName).ThenBy(a => a.Item.Sku)
                .Select(a => a.Item)
                .ToList();

            return new StockStatusReportDto(usageDays, usageFrom, usageTo, all.Count,
                all.Count(a => a.Status == StockStatus.InStock), all.Count(a => a.Status == StockStatus.LowStock),
                all.Count(a => a.Status == StockStatus.OutOfStock),
                includeCosts ? all.Sum(a => a.Value) : null,
                includeCosts ? all.Count(a => a.CostMissing) : null,
                byType, items);
        }

        // =====================================================================
        // Inventory: movements
        // =====================================================================

        /// <summary>Per product that moved in the period: opening, what came in and went out by reason, closing.</summary>
        public async Task<StockMovementReportDto> GetMovementReportAsync(DateOnly from, DateOnly to, int utcOffsetMinutes,
            Guid? productId, bool includeCosts)
        {
            var period = ReportPeriod.Create(from, to, utcOffsetMinutes);

            var totals = await _repository.GetStockMovementTotalsAsync(period.FromUtc, period.ToUtcExclusive, productId);
            var opening = await _repository.GetOpeningStockBalancesAsync(period.FromUtc, productId);

            return BuildMovementReport(period, opening, totals, includeCosts);
        }

        public static StockMovementReportDto BuildMovementReport(ReportPeriod period, IReadOnlyDictionary<Guid, decimal> openingBalances,
            IReadOnlyList<StockMovementTotalRow> totals, bool includeCosts)
        {
            var items = totals.GroupBy(t => (t.ProductId, t.ProductName, t.Sku, t.BaseUnitSymbol))
                .Select(g =>
                {
                    decimal Qty(StockMovementType type, bool? increase = null) =>
                        g.Where(t => t.MovementType == type && (!increase.HasValue || t.IsIncrease == increase.Value)).Sum(t => t.Quantity);
                    decimal Val(StockMovementType type) => g.Where(t => t.MovementType == type).Sum(t => t.Value);

                    var opening = openingBalances.GetValueOrDefault(g.Key.ProductId);
                    var received = Qty(StockMovementType.PurchaseReceipt);
                    var produced = Qty(StockMovementType.ProductionOutput);
                    var sold = -(Qty(StockMovementType.Sale) + Qty(StockMovementType.SaleVoid));
                    var consumed = -Qty(StockMovementType.ProductionConsumption);
                    var adjustedIn = Qty(StockMovementType.Adjustment, true);
                    var adjustedOut = -Qty(StockMovementType.Adjustment, false);

                    // Closing comes from every movement, not from the columns above, so a movement type this report does not
                    // name (a revaluation moves no quantity today) can never make the row stop adding up to the real balance.
                    var closing = opening + g.Sum(t => t.Quantity);

                    return new StockMovementReportRowDto(g.Key.ProductId, g.Key.ProductName, g.Key.Sku, g.Key.BaseUnitSymbol,
                        opening, received, produced, sold, consumed, adjustedIn, adjustedOut, closing, g.Sum(t => t.MovementCount),
                        includeCosts ? CostingCalculator.RoundMoney(Val(StockMovementType.PurchaseReceipt)) : null,
                        includeCosts ? CostingCalculator.RoundMoney(-(Val(StockMovementType.Sale) + Val(StockMovementType.SaleVoid))) : null,
                        includeCosts ? CostingCalculator.RoundMoney(-Val(StockMovementType.ProductionConsumption)) : null,
                        includeCosts ? CostingCalculator.RoundMoney(Val(StockMovementType.Adjustment)) : null);
                })
                .OrderBy(r => r.ProductName).ThenBy(r => r.Sku)
                .ToList();

            return new StockMovementReportDto(period.From, period.To, items.Count,
                includeCosts ? items.Sum(i => i.ReceivedValue ?? 0m) : null,
                includeCosts ? items.Sum(i => i.SoldCost ?? 0m) : null,
                includeCosts ? items.Sum(i => i.ConsumedCost ?? 0m) : null,
                includeCosts ? items.Sum(i => i.AdjustmentNetValue ?? 0m) : null,
                items);
        }

        // =====================================================================
        // Inventory: adjustments
        // =====================================================================

        /// <param name="rows">How many adjustments to list, newest first (1..500, default 100). Totals always cover all of them.</param>
        public async Task<StockAdjustmentReportDto> GetAdjustmentReportAsync(DateOnly from, DateOnly to, int utcOffsetMinutes,
            Guid? productId, int rows, bool includeCosts)
        {
            var period = ReportPeriod.Create(from, to, utcOffsetMinutes);
            var adjustments = await _repository.GetStockAdjustmentsAsync(period.FromUtc, period.ToUtcExclusive, productId);

            var limit = ReportLimits.ClampRows(rows);
            var userIds = adjustments.OrderByDescending(a => a.CreatedAt).Take(limit)
                .Where(a => a.CreatedBy.HasValue).Select(a => a.CreatedBy!.Value).Distinct().ToList();
            var names = userIds.Count == 0 ? new Dictionary<Guid, string>() : await _repository.GetUserNamesAsync(userIds);

            return BuildAdjustmentReport(period, adjustments, names, limit, includeCosts);
        }

        public static StockAdjustmentReportDto BuildAdjustmentReport(ReportPeriod period, IReadOnlyList<StockAdjustmentRow> adjustments,
            IReadOnlyDictionary<Guid, string> userNames, int limit, bool includeCosts)
        {
            decimal? ValueOf(StockAdjustmentRow a) => a.UnitCost.HasValue ? CostingCalculator.LineCost(a.QuantityChange, a.UnitCost.Value) : null;

            var increases = adjustments.Where(a => a.QuantityChange > 0).ToList();
            var decreases = adjustments.Where(a => a.QuantityChange < 0).ToList();
            var increaseValue = increases.Sum(a => ValueOf(a) ?? 0m);
            var decreaseValue = -decreases.Sum(a => ValueOf(a) ?? 0m);

            var items = adjustments.OrderByDescending(a => a.CreatedAt).ThenBy(a => a.ProductName)
                .Take(limit)
                .Select(a => new StockAdjustmentItemDto(a.StockMovementId, a.CreatedAt, period.LocalDateOf(a.CreatedAt), a.ProductId,
                    a.ProductName, a.Sku, a.BaseUnitSymbol, a.QuantityChange, a.QuantityChange > 0 ? "Increase" : "Decrease",
                    includeCosts ? a.UnitCost : null, includeCosts ? ValueOf(a) : null, a.Reason, a.CreatedBy,
                    a.CreatedBy.HasValue && userNames.TryGetValue(a.CreatedBy.Value, out var name) ? name : null))
                .ToList();

            return new StockAdjustmentReportDto(period.From, period.To, adjustments.Count, increases.Count, decreases.Count,
                includeCosts ? increaseValue : null, includeCosts ? decreaseValue : null,
                includeCosts ? increaseValue - decreaseValue : null,
                includeCosts ? adjustments.Count(a => !a.UnitCost.HasValue) : null,
                adjustments.Count > items.Count, items);
        }

        // =====================================================================
        // Purchasing: summary
        // =====================================================================

        /// <param name="top">How many suppliers and products to list (1..50).</param>
        public async Task<PurchaseSummaryDto> GetPurchaseSummaryAsync(DateOnly from, DateOnly to, int utcOffsetMinutes, Guid? supplierId, int top)
        {
            var period = ReportPeriod.Create(from, to, utcOffsetMinutes);
            var lines = await _repository.GetPurchaseLinesAsync(period.FromUtc, period.ToUtcExclusive, supplierId, null);
            return BuildPurchaseSummary(period, lines, ReportLimits.ClampTop(top));
        }

        public static PurchaseSummaryDto BuildPurchaseSummary(ReportPeriod period, IReadOnlyList<PurchaseLineRow> lines, int top)
        {
            var committed = lines.Where(l => IsCommitted(l.Status)).ToList();
            var cancelled = lines.Where(l => l.Status == PurchaseOrderStatus.Cancelled).ToList();

            var orderedValue = committed.Sum(OrderedValue);
            var receivedValue = committed.Sum(ReceivedValue);

            var byStatus = lines.GroupBy(l => l.Status).OrderBy(g => g.Key)
                .Select(g => new PurchaseByStatusDto(g.Key.ToString(), g.Select(l => l.PurchaseOrderId).Distinct().Count(), g.Sum(OrderedValue)))
                .ToList();

            var bySupplier = committed.GroupBy(l => (l.SupplierId, l.SupplierName))
                .Select(g => new PurchaseBySupplierDto(g.Key.SupplierId, g.Key.SupplierName,
                    g.Select(l => l.PurchaseOrderId).Distinct().Count(), g.Sum(OrderedValue), g.Sum(ReceivedValue), g.Sum(OutstandingValue),
                    Percent(g.Sum(OrderedValue), orderedValue)))
                .OrderByDescending(s => s.OrderedValue).ThenBy(s => s.SupplierName)
                .Take(top)
                .ToList();

            var topProducts = committed.GroupBy(l => (l.ProductId, l.ProductName, l.Sku, l.UnitOfMeasureId, l.UnitSymbol))
                .Select(g =>
                {
                    var quantity = g.Sum(l => l.QuantityOrdered);
                    var value = g.Sum(OrderedValue);
                    return new PurchaseByProductDto(g.Key.ProductId, g.Key.ProductName, g.Key.Sku, g.Key.UnitOfMeasureId, g.Key.UnitSymbol,
                        quantity, g.Sum(l => l.QuantityReceived), value, WeightedUnitCost(g), Percent(value, orderedValue));
                })
                .OrderByDescending(p => p.OrderedValue).ThenBy(p => p.ProductName).ThenBy(p => p.UnitSymbol)
                .Take(top)
                .ToList();

            return new PurchaseSummaryDto(period.From, period.To,
                lines.Select(l => l.PurchaseOrderId).Distinct().Count(),
                cancelled.Select(l => l.PurchaseOrderId).Distinct().Count(), cancelled.Sum(OrderedValue),
                orderedValue, receivedValue, committed.Sum(OutstandingValue), committed.Count(l => l.UnitCost <= 0),
                byStatus, bySupplier, topProducts);
        }

        // =====================================================================
        // Purchasing: open orders
        // =====================================================================

        public async Task<OpenOrdersReportDto> GetOpenOrdersAsync(Guid? supplierId, int utcOffsetMinutes)
        {
            var today = ReportPeriod.TodayAt(utcOffsetMinutes); // also validates the offset
            var lines = await _repository.GetOpenPurchaseLinesAsync(supplierId);
            return BuildOpenOrders(today, TimeSpan.FromMinutes(utcOffsetMinutes), lines);
        }

        public static OpenOrdersReportDto BuildOpenOrders(DateOnly today, TimeSpan offset, IReadOnlyList<PurchaseLineRow> lines)
        {
            var orders = lines
                .Where(l => l.Status is PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived)
                .GroupBy(l => l.PurchaseOrderId)
                .Select(g =>
                {
                    var head = g.First();
                    var openLines = g.Where(l => l.QuantityOrdered > l.QuantityReceived)
                        .OrderBy(l => l.ProductName).ThenBy(l => l.UnitSymbol)
                        .Select(l => new OpenOrderLineDto(l.ProductId, l.ProductName, l.Sku, l.UnitSymbol, l.QuantityOrdered,
                            l.QuantityReceived, l.QuantityOrdered - l.QuantityReceived, l.UnitCost, OutstandingValue(l)))
                        .ToList();

                    var orderDate = ReportPeriod.ToLocalDate(head.OrderDate, offset);
                    DateOnly? expected = head.ExpectedDeliveryDate.HasValue
                        ? ReportPeriod.ToLocalDate(head.ExpectedDeliveryDate.Value, offset)
                        : null;
                    var overdue = expected.HasValue && expected.Value < today;

                    return new OpenOrderDto(head.PurchaseOrderId, head.OrderNumber, head.SupplierId, head.SupplierName, head.Status.ToString(),
                        orderDate, Math.Max(0, today.DayNumber - orderDate.DayNumber), expected, overdue,
                        overdue ? today.DayNumber - expected!.Value.DayNumber : 0,
                        openLines.Sum(l => l.OutstandingValue), openLines);
                })
                .Where(o => o.Lines.Any()) // a status that has not caught up with fully received lines is not "open"
                .OrderBy(o => o.IsOverdue ? 0 : o.ExpectedDeliveryDate.HasValue ? 1 : 2)
                .ThenByDescending(o => o.DaysOverdue)
                .ThenBy(o => o.ExpectedDeliveryDate ?? DateOnly.MaxValue)
                .ThenBy(o => o.OrderDate).ThenBy(o => o.OrderNumber)
                .ToList();

            return new OpenOrdersReportDto(today, orders.Count, orders.Count(o => o.IsOverdue),
                orders.Sum(o => o.OutstandingValue), orders.Where(o => o.IsOverdue).Sum(o => o.OutstandingValue), orders);
        }

        // =====================================================================
        // Purchasing: price trends
        // =====================================================================

        /// <param name="rows">How many products to list (1..500, default 100); for a single product, how many of its latest purchases.</param>
        public async Task<PriceTrendsReportDto> GetPriceTrendsAsync(DateOnly from, DateOnly to, int utcOffsetMinutes,
            Guid? productId, Guid? supplierId, int rows)
        {
            var period = ReportPeriod.Create(from, to, utcOffsetMinutes);
            var lines = await _repository.GetPurchaseLinesAsync(period.FromUtc, period.ToUtcExclusive, supplierId, productId);
            return BuildPriceTrends(period, lines, productId.HasValue, ReportLimits.ClampRows(rows));
        }

        public static PriceTrendsReportDto BuildPriceTrends(ReportPeriod period, IReadOnlyList<PurchaseLineRow> lines, bool includePoints, int rows)
        {
            var items = lines
                .Where(l => IsCommitted(l.Status) && l.UnitCost > 0 && l.QuantityOrdered > 0)
                .GroupBy(l => (l.ProductId, l.ProductName, l.Sku, l.UnitOfMeasureId, l.UnitSymbol))
                .Select(g =>
                {
                    var byDate = g.OrderBy(l => l.OrderDate).ThenBy(l => l.OrderNumber).ToList();
                    var first = byDate.First();
                    var latest = byDate.Last();
                    var lowest = g.OrderBy(l => l.UnitCost).ThenByDescending(l => l.OrderDate).First();
                    var highest = g.OrderByDescending(l => l.UnitCost).ThenByDescending(l => l.OrderDate).First();

                    return new PriceTrendDto(g.Key.ProductId, g.Key.ProductName, g.Key.Sku, g.Key.UnitOfMeasureId, g.Key.UnitSymbol,
                        byDate.Count, g.Select(l => l.SupplierId).Distinct().Count(), g.Sum(l => l.QuantityOrdered), WeightedUnitCost(g),
                        first.UnitCost, latest.UnitCost, period.LocalDateOf(latest.OrderDate), latest.SupplierName,
                        lowest.UnitCost, lowest.SupplierName, highest.UnitCost, highest.SupplierName,
                        byDate.Count > 1 ? Percent(latest.UnitCost - first.UnitCost, first.UnitCost) : null,
                        includePoints
                            ? byDate.TakeLast(rows).Select(l => new PricePointDto(period.LocalDateOf(l.OrderDate), l.OrderNumber,
                                l.SupplierId, l.SupplierName, l.QuantityOrdered, l.UnitCost)).ToList()
                            : null);
                })
                // Biggest movers first, up or down: that is where a price needs a second look.
                .OrderByDescending(t => Math.Abs(t.ChangePercent ?? 0m)).ThenBy(t => t.ProductName).ThenBy(t => t.UnitSymbol)
                .ToList();

            return new PriceTrendsReportDto(period.From, period.To, items.Count, items.Take(rows).ToList());
        }

        // =====================================================================
        // Purchasing helpers
        // =====================================================================

        // Draft orders are not commitments yet and cancelled ones never were.
        private static bool IsCommitted(PurchaseOrderStatus status) =>
            status is PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received;

        // Every value is rounded per line, so totals are exact sums of the rounded lines and OrderedValue = ReceivedValue + OutstandingValue.
        private static decimal OrderedValue(PurchaseLineRow l) => CostingCalculator.LineCost(l.QuantityOrdered, l.UnitCost);
        private static decimal ReceivedValue(PurchaseLineRow l) => CostingCalculator.LineCost(l.QuantityReceived, l.UnitCost);
        private static decimal OutstandingValue(PurchaseLineRow l) => Math.Max(0m, OrderedValue(l) - ReceivedValue(l));

        /// <summary>Average price per unit weighted by quantity: total spend / total quantity.</summary>
        private static decimal WeightedUnitCost(IEnumerable<PurchaseLineRow> lines)
        {
            var list = lines.ToList();
            var quantity = list.Sum(l => l.QuantityOrdered);
            return quantity == 0 ? 0m : Math.Round(list.Sum(l => l.QuantityOrdered * l.UnitCost) / quantity, 4, MidpointRounding.AwayFromZero);
        }
    }
}
