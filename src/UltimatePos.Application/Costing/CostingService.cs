using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Costing.Dtos;
using UltimatePos.Application.Inventory;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Costing
{
    public class CostingService
    {
        private const int MaxProfitRangeDays = 366;

        private readonly ICostingRepository _repository;
        private readonly IInventoryRepository _inventoryRepository;
        private readonly ICatalogRepository _catalogRepository;
        private readonly ICurrentUser _currentUser;

        public CostingService(ICostingRepository repository, IInventoryRepository inventoryRepository,
            ICatalogRepository catalogRepository, ICurrentUser currentUser)
        {
            _repository = repository;
            _inventoryRepository = inventoryRepository;
            _catalogRepository = catalogRepository;
            _currentUser = currentUser;
        }

        // ---- What the stock on the shelf is worth ----

        public async Task<StockValuationDto> GetStockValuationAsync(ItemType? itemType)
        {
            var levels = (await _inventoryRepository.GetStockLevelsAsync(false))
                .Where(s => s.QuantityOnHand != 0 && (!itemType.HasValue || s.Product.ItemType == itemType.Value))
                .ToList();

            var rows = levels
                .Select(s => new StockValuationRowDto(
                    s.ProductId, s.Product.Name, s.Product.Sku, s.Product.ItemType.ToString(),
                    s.QuantityOnHand, s.AverageUnitCost,
                    CostingCalculator.LineCost(s.QuantityOnHand, s.AverageUnitCost),
                    CostMissing: s.QuantityOnHand > 0 && s.AverageUnitCost == 0))
                .OrderByDescending(r => r.StockValue).ThenBy(r => r.ProductName)
                .ToList();

            var totals = rows.GroupBy(r => r.ItemType)
                .Select(g => new StockValuationTotalDto(g.Key, g.Count(), g.Sum(r => r.StockValue)))
                .OrderBy(t => t.ItemType)
                .ToList();

            return new StockValuationDto(rows, totals, rows.Sum(r => r.StockValue), rows.Count(r => r.CostMissing));
        }

        // ---- Price list vs cost: is anything priced below what it costs us? ----

        public async Task<IEnumerable<MarginRowDto>> GetMarginsAsync(PriceType? priceType)
        {
            var tiers = await _repository.GetCurrentPriceTiersAsync(priceType);

            // Price history is never overwritten, so several tiers can be "in effect"; the latest one wins.
            var current = tiers
                .Where(t => t.Product.IsActive)
                .GroupBy(t => (t.ProductId, t.UnitOfMeasureId, t.PriceType))
                .Select(g => g.OrderByDescending(t => t.EffectiveFrom).First())
                .ToList();
            if (current.Count == 0)
                return Array.Empty<MarginRowDto>();

            var productIds = current.Select(t => t.ProductId).Distinct().ToList();
            var averageCosts = await _inventoryRepository.GetAverageCostsAsync(productIds);
            var factors = await _repository.GetConversionFactorsAsync(productIds);

            var rows = new List<MarginRowDto>();
            foreach (var tier in current)
            {
                decimal factor;
                if (tier.Product.BaseUnitOfMeasureId == tier.UnitOfMeasureId) factor = 1m;
                else if (!factors.TryGetValue((tier.ProductId, tier.UnitOfMeasureId), out factor)) continue; // priced in a unit with no conversion: cost cannot be compared

                averageCosts.TryGetValue(tier.ProductId, out var averagePerBase);
                var costPerPriceUnit = CostingCalculator.RoundMoney(averagePerBase * factor);
                var (margin, percent) = CostingCalculator.Margin(tier.Price, costPerPriceUnit);

                rows.Add(new MarginRowDto(tier.ProductId, tier.Product.Name, tier.Product.Sku, tier.UnitOfMeasureId,
                    tier.UnitOfMeasure.Symbol, tier.PriceType.ToString(), tier.Price, costPerPriceUnit, margin, percent,
                    CostMissing: averagePerBase == 0));
            }

            return rows.OrderBy(r => r.ProductName).ThenBy(r => r.PriceType).ThenBy(r => r.UnitSymbol).ToList();
        }

        // ---- What we actually made on what we sold ----

        /// <param name="from">Inclusive.</param>
        /// <param name="to">Exclusive. Both are exact instants, so "today in Nairobi" is unambiguous: send +03:00 offsets.</param>
        public async Task<ProfitReportDto> GetProfitAsync(DateTimeOffset from, DateTimeOffset to, Guid? productId)
        {
            if (to <= from)
                throw new InvalidAssignmentException("'to' must be after 'from'.");
            if ((to - from).TotalDays > MaxProfitRangeDays)
                throw new InvalidAssignmentException($"The range cannot exceed {MaxProfitRangeDays} days — narrow it or run the report per period.");

            var lines = await _repository.GetSaleCostRowsAsync(from.UtcDateTime, to.UtcDateTime, productId);

            var items = lines.GroupBy(l => (l.ProductId, l.ProductName, l.Sku))
                .Select(g =>
                {
                    var revenue = g.Sum(l => l.LineTotal);
                    var costed = g.Where(IsCosted).ToList();
                    var uncostedRevenue = g.Where(l => !IsCosted(l)).Sum(l => l.LineTotal);
                    var cost = costed.Sum(l => CostingCalculator.LineCost(l.QuantityInBaseUnits, l.UnitCost!.Value));
                    var costedRevenue = costed.Sum(l => l.LineTotal);
                    var profit = costedRevenue - cost;
                    return new ProfitRowDto(g.Key.ProductId, g.Key.ProductName, g.Key.Sku, g.Sum(l => l.QuantityInBaseUnits),
                        revenue, cost, profit, Percent(profit, costedRevenue), uncostedRevenue);
                })
                .OrderByDescending(r => r.GrossProfit).ThenBy(r => r.ProductName)
                .ToList();

            var totalRevenue = items.Sum(i => i.Revenue);
            var totalUncosted = items.Sum(i => i.UncostedRevenue);
            var totalCost = items.Sum(i => i.Cost);
            var totalProfit = items.Sum(i => i.GrossProfit);

            return new ProfitReportDto(from, to, items, totalRevenue, totalCost, totalProfit,
                Percent(totalProfit, totalRevenue - totalUncosted), totalUncosted);
        }

        // A zero cost means "no cost was recorded", not "free" — treating it as costed would show a 100% margin.
        private static bool IsCosted(SaleCostRow l) => l.UnitCost is > 0;

        private static decimal? Percent(decimal profit, decimal revenue) =>
            revenue == 0 ? null : Math.Round(profit / revenue * 100m, 2, MidpointRounding.AwayFromZero);

        // ---- Setting a cost on stock that already exists (onboarding / correcting a wrong cost) ----

        public async Task<StockValuationRowDto> RevalueStockAsync(RevalueStockRequestDto request)
        {
            if (request.UnitCost <= 0)
                throw new InvalidAssignmentException("Unit cost must be greater than zero.");
            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new InvalidAssignmentException("A reason is required for every revaluation.");

            if (await _catalogRepository.GetProductByIdAsync(request.ProductId) is null)
                throw new NotFoundException($"Product '{request.ProductId}' not found.");

            var factor = await _catalogRepository.GetConversionFactorAsync(request.ProductId, request.UnitOfMeasureId)
                ?? throw new InvalidAssignmentException("This product has no conversion for the unit given.");

            var perBase = CostingCalculator.ToUnitCostPerBaseUnit(request.UnitCost, factor);
            if (perBase <= 0)
                throw new InvalidAssignmentException("Unit cost is too small — it rounds to zero per base unit.");

            await _inventoryRepository.RevalueStockAsync(request.ProductId, perBase, request.Reason.Trim(), _currentUser.UserId);

            var level = await _inventoryRepository.GetStockLevelAsync(request.ProductId)
                ?? throw new NotFoundException($"No stock record for product '{request.ProductId}'.");
            return new StockValuationRowDto(level.ProductId, level.Product.Name, level.Product.Sku, level.Product.ItemType.ToString(),
                level.QuantityOnHand, level.AverageUnitCost, CostingCalculator.LineCost(level.QuantityOnHand, level.AverageUnitCost),
                CostMissing: level.QuantityOnHand > 0 && level.AverageUnitCost == 0);
        }
    }
}