using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Notifications;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Costing;
using UltimatePos.Application.Inventory.Dtos;
using UltimatePos.Application.Reports;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Application.Inventory
{
    public class InventoryService
    {
        public const int DefaultNearExpiryDays = 90;
        private const string CostingViewPermission = "Costing.View";

        private readonly IInventoryRepository _repository;
        private readonly ICatalogRepository _catalogRepository;
        private readonly ICurrentUser _currentUser;
        private readonly ISystemEventRecorder _events;
        private readonly IBranchContext _branches;

        public InventoryService(IInventoryRepository repository, ICatalogRepository catalogRepository, ICurrentUser currentUser,
            ISystemEventRecorder events, IBranchContext branches)
        {
            _branches = branches;
            _events = events;
            _repository = repository;
            _catalogRepository = catalogRepository;
            _currentUser = currentUser;
        }

        private static DateOnly Today => ReportPeriod.TodayAt(ReportPeriod.DefaultUtcOffsetMinutes);

        public async Task<IEnumerable<StockLevelDto>> GetStockLevelsAsync(bool belowReorderOnly)
        {
            var levels = (await _repository.GetStockLevelsAsync(belowReorderOnly)).ToList();
            var sellable = await _repository.GetSellableByBranchAsync(levels.Where(l => l.Product.IsBatchTracked).Select(l => l.ProductId), Today);
            var reserved = await _repository.GetReservedQuantitiesAsync(levels.Select(l => l.ProductId));
            return levels.OrderBy(l => l.Product.Name).ThenBy(l => l.Branch?.BranchNumber).Select(s => ToDto(s, sellable, reserved));
        }

        public async Task<StockLevelDto> GetStockLevelAsync(Guid productId)
        {
            var level = await _repository.GetStockLevelAsync(productId)
                ?? throw new NotFoundException($"No stock record for product '{productId}' at this branch — nothing has been received, produced or adjusted against it here yet.");
            var sellable = await _repository.GetSellableByBranchAsync(new[] { productId }, Today);
            var reserved = await _repository.GetReservedQuantitiesAsync(new[] { productId });
            return ToDto(level, sellable, reserved);
        }

        private static StockLevelDto ToDto(StockLevel s, IReadOnlyDictionary<(Guid, Guid), decimal> sellable,
            IReadOnlyDictionary<(Guid, Guid), decimal> reserved)
        {
            var held = reserved.GetValueOrDefault((s.ProductId, s.BranchId));
            var usable = s.Product.IsBatchTracked ? sellable.GetValueOrDefault((s.ProductId, s.BranchId)) : s.QuantityOnHand;
            return new(s.ProductId, s.Product.Name, s.Product.Sku, s.QuantityOnHand, s.Product.ReorderLevel, s.QuantityOnHand <= s.Product.ReorderLevel,
                s.Product.IsBatchTracked, usable - held,
                s.Product.BaseUnitOfMeasure?.Symbol, s.BranchId, s.Branch is null ? null : Branch.FormatKraBranchId(s.Branch.BranchNumber), s.Branch?.Name,
                held);
        }

        public async Task<IEnumerable<StockMovementDto>> GetStockMovementsAsync(Guid productId)
        {
            var movements = (await _repository.GetStockMovementsAsync(productId)).ToList();
            var batchNumbers = (await _repository.GetBatchesAsync(productId, includeEmpty: true)).ToDictionary(b => b.StockBatchId, b => b.BatchNumber);
            return movements.Select(m => new StockMovementDto(m.StockMovementId, m.ProductId, m.MovementType.ToString(), m.QuantityChange, m.ReferenceType,
                m.ReferenceId, m.BatchId, m.Notes, m.CreatedAt, m.StockBatchId,
                m.StockBatchId is { } id && batchNumbers.TryGetValue(id, out var number) ? number : null, m.BranchId));
        }

        // Opening balances and corrections. Positive or negative; reason is mandatory (audit trail).
        public async Task<StockLevelDto> AdjustStockAsync(CreateStockAdjustmentRequestDto request)
        {
            if (request.QuantityChange == 0)
                throw new ValidationException("quantityChange", "Quantity change cannot be zero.");
            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new ValidationException("reason", "A reason is required for every stock adjustment.");

            var product = await _catalogRepository.GetProductByIdAsync(request.ProductId)
                ?? throw new NotFoundException($"Product '{request.ProductId}' not found.");

            var factor = await _catalogRepository.GetConversionFactorAsync(request.ProductId, request.UnitOfMeasureId)
                ?? throw new InvalidAssignmentException("This product has no conversion for the unit given.");

            var changeInBaseUnits = Math.Round(request.QuantityChange * factor, 4, MidpointRounding.AwayFromZero);
            if (changeInBaseUnits == 0)
               throw new InvalidAssignmentException("Quantity is too small — it rounds to zero in the product's base unit.");

            decimal? unitCostPerBase = null;
            if (request.UnitCost.HasValue)
            {
                if (changeInBaseUnits < 0)
                    throw new InvalidAssignmentException("A unit cost only applies when adding stock — removals are valued at the current average cost.");
                if (request.UnitCost.Value <= 0)
                    throw new InvalidAssignmentException("Unit cost must be greater than zero.");

                unitCostPerBase = CostingCalculator.ToUnitCostPerBaseUnit(request.UnitCost.Value, factor);
                if (unitCostPerBase <= 0)
                    throw new InvalidAssignmentException("Unit cost is too small — it rounds to zero per base unit.");
            }
            else if (changeInBaseUnits > 0)
            {
                // Adding stock with no cost anywhere would value it at nothing and quietly corrupt every margin downstream.
                var level = await _repository.GetStockLevelAsync(request.ProductId);
                if (level is null || level.AverageUnitCost <= 0)
                    throw new InvalidAssignmentException(
                        "This product has no cost yet — provide a unit cost (the cost of one unit of the unit given) with this adjustment.");
            }

            var batch = await ResolveBatchAsync(product, request, changeInBaseUnits);
            _events.Record(new SystemEventDraft(SystemEventTypes.StockAdjusted,
                $"Stock of {product.Name} adjusted by {changeInBaseUnits:+0.####;-0.####} {product.BaseUnitOfMeasure?.Symbol}: {request.Reason.Trim()}",
                EntityType: "Product", EntityId: product.ProductId,
                Data: new { changeInBaseUnits, reason = request.Reason.Trim(), batch?.StockBatchId, batch?.BatchNumber }));
            await _repository.AdjustStockAsync(request.ProductId, changeInBaseUnits, request.Reason.Trim(), _currentUser.UserId, unitCostPerBase, batch);
            return await GetStockLevelAsync(request.ProductId);
        }

        /// <summary>Batch-tracked products are always adjusted lot by lot, so the batches keep adding up to the product's total.</summary>
        private async Task<BatchTarget?> ResolveBatchAsync(Product product, CreateStockAdjustmentRequestDto request, decimal changeInBaseUnits)
        {
            var batchGiven = request.StockBatchId.HasValue || !string.IsNullOrWhiteSpace(request.BatchNumber);
            if (!product.IsBatchTracked)
            {
                if (batchGiven)
                    throw new ValidationException("stockBatchId", $"'{product.Name}' is not batch-tracked — leave the batch out.");
                return null;
            }

            if (request.StockBatchId is { } batchId)
            {
                var existing = await _repository.GetBatchAsync(batchId);
                if (existing is null || existing.ProductId != product.ProductId)
                    throw new ValidationException("stockBatchId", "That batch does not belong to this product.");
                if (existing.BranchId != _branches.RequireActiveBranchId())
                    throw new ValidationException("stockBatchId", "That batch is held at another branch — switch to that branch to adjust it.");
                if (changeInBaseUnits < 0 && existing.QuantityOnHand < -changeInBaseUnits)
                    throw new ValidationException("quantityChange", $"Batch '{existing.BatchNumber}' only holds {existing.QuantityOnHand:0.####}.");
                return new BatchTarget(batchId, null, null, null);
            }

            if (changeInBaseUnits < 0)
                throw new ValidationException("stockBatchId", $"Choose the batch of '{product.Name}' to remove stock from.");
            if (string.IsNullOrWhiteSpace(request.BatchNumber))
                throw new ValidationException("batchNumber", $"'{product.Name}' is batch-tracked — give the batch number (and expiry date) of the stock being added.");

            var number = request.BatchNumber.Trim();
            if (number.Length > 64)
                throw new ValidationException("batchNumber", "A batch number is at most 64 characters.");
            if (request.ExpiryDate is { } expiry && request.ManufactureDate is { } made && made > expiry)
                throw new ValidationException("manufactureDate", "The manufacture date cannot be after the expiry date.");
            return new BatchTarget(null, number, request.ExpiryDate, request.ManufactureDate);
        }

        // ---- Batches and expiry ----

        /// <param name="expiringWithinDays">Only batches that expire within this many days (expired ones included).</param>
        public async Task<IEnumerable<StockBatchDto>> GetBatchesAsync(Guid? productId, int? expiringWithinDays, bool includeExpired, bool includeEmpty)
        {
            if (expiringWithinDays is < 0 or > 3650)
                throw new ValidationException("expiringWithinDays", "expiringWithinDays must be between 0 and 3650.");

            var today = Today;
            var window = expiringWithinDays ?? DefaultNearExpiryDays;
            var showCost = _currentUser.HasPermission(CostingViewPermission);

            return (await _repository.GetBatchesAsync(productId, includeEmpty))
                .Where(b => includeExpired || b.ExpiryDate is null || b.ExpiryDate >= today)
                .Where(b => expiringWithinDays is null || (b.ExpiryDate is { } e && e <= today.AddDays(expiringWithinDays.Value)))
                .OrderBy(b => b.ExpiryDate ?? DateOnly.MaxValue).ThenBy(b => b.Product.Name).ThenBy(b => b.BatchNumber)
                .Select(b => new StockBatchDto(b.StockBatchId, b.ProductId, b.Product.Name, b.Product.Sku, b.BatchNumber, b.ExpiryDate, b.ManufactureDate,
                    b.QuantityOnHand, b.Product.BaseUnitOfMeasure?.Symbol, showCost ? b.UnitCost : null,
                    b.ExpiryDate is { } d ? d.DayNumber - today.DayNumber : null, ExpiryStatus(b.ExpiryDate, today, window),
                    b.SupplierId, b.PurchaseOrderId, b.CreatedAt, b.BranchId,
                    b.Branch is null ? null : Branch.FormatKraBranchId(b.Branch.BranchNumber), b.Branch?.Name));
        }

        /// <summary>Stock that has expired or will within <paramref name="withinDays"/> days, soonest first — what to sell, return or write off.</summary>
        public async Task<ExpiryReportDto> GetExpiryReportAsync(int withinDays)
        {
            if (withinDays is < 0 or > 3650)
                throw new ValidationException("withinDays", "withinDays must be between 0 and 3650.");

            var today = Today;
            var batches = (await _repository.GetBatchesAsync(null, includeEmpty: false))
                .Where(b => b.ExpiryDate is { } e && e <= today.AddDays(withinDays))
                .ToList();
            var showCost = _currentUser.HasPermission(CostingViewPermission);
            var costs = showCost
                ? await _repository.GetAverageCostsByBranchAsync(batches.Select(b => b.ProductId))
                : new Dictionary<(Guid, Guid), decimal>();

            var items = batches
                .OrderBy(b => b.ExpiryDate).ThenBy(b => b.Product.Name)
                .Select(b => new ExpiryReportItemDto(b.StockBatchId, b.ProductId, b.Product.Name, b.Product.Sku, b.BatchNumber, b.ExpiryDate!.Value,
                    b.ExpiryDate!.Value.DayNumber - today.DayNumber, ExpiryStatus(b.ExpiryDate, today, withinDays), b.QuantityOnHand,
                    b.Product.BaseUnitOfMeasure?.Symbol,
                    showCost ? Math.Round(b.QuantityOnHand * costs.GetValueOrDefault((b.ProductId, b.BranchId)), 2, MidpointRounding.AwayFromZero) : null,
                    b.BranchId, b.Branch is null ? null : Branch.FormatKraBranchId(b.Branch.BranchNumber), b.Branch?.Name))
                .ToList();

            var expired = items.Where(i => i.ExpiryStatus == "Expired").ToList();
            var near = items.Where(i => i.ExpiryStatus == "NearExpiry").ToList();
            return new ExpiryReportDto(today, withinDays, expired.Count, showCost ? expired.Sum(i => i.Value ?? 0) : null,
                near.Count, showCost ? near.Sum(i => i.Value ?? 0) : null, items);
        }

        private static string ExpiryStatus(DateOnly? expiry, DateOnly today, int nearExpiryDays) =>
            expiry is not { } e ? "NoExpiry"
            : e < today ? "Expired"
            : e <= today.AddDays(nearExpiryDays) ? "NearExpiry"
            : "Ok";
    }
}
