using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Inventory.Dtos;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Application.Inventory
{
    public class InventoryService
    {
        private readonly IInventoryRepository _repository;
        private readonly ICatalogRepository _catalogRepository;
        private readonly ICurrentUser _currentUser;

        public InventoryService(IInventoryRepository repository, ICatalogRepository catalogRepository, ICurrentUser currentUser)
        {
            _repository = repository;
            _catalogRepository = catalogRepository;
            _currentUser = currentUser;
        }

        public async Task<IEnumerable<StockLevelDto>> GetStockLevelsAsync(bool belowReorderOnly) =>
            (await _repository.GetStockLevelsAsync(belowReorderOnly))
                .Select(s => new StockLevelDto(s.ProductId, s.Product.Name, s.Product.Sku, s.QuantityOnHand, s.Product.ReorderLevel, s.QuantityOnHand <= s.Product.ReorderLevel));

        public async Task<StockLevelDto> GetStockLevelAsync(Guid productId)
        {
            var level = await _repository.GetStockLevelAsync(productId)
                ?? throw new NotFoundException($"No stock record for product '{productId}' — nothing has been received, produced or adjusted against it yet.");
            return new StockLevelDto(level.ProductId, level.Product.Name, level.Product.Sku, level.QuantityOnHand, level.Product.ReorderLevel, level.QuantityOnHand <= level.Product.ReorderLevel);
        }

        public async Task<IEnumerable<StockMovementDto>> GetStockMovementsAsync(Guid productId) =>
            (await _repository.GetStockMovementsAsync(productId))
                .Select(m => new StockMovementDto(m.StockMovementId, m.ProductId, m.MovementType.ToString(), m.QuantityChange, m.ReferenceType, m.ReferenceId, m.BatchId, m.Notes, m.CreatedAt));

        // Opening balances and corrections. Positive or negative; reason is mandatory (audit trail).
        public async Task<StockLevelDto> AdjustStockAsync(CreateStockAdjustmentRequestDto request)
        {
            if (request.QuantityChange == 0)
                throw new InvalidAssignmentException("Quantity change cannot be zero.");
            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new InvalidAssignmentException("A reason is required for every stock adjustment.");

            if (await _catalogRepository.GetProductByIdAsync(request.ProductId) is null)
                throw new NotFoundException($"Product '{request.ProductId}' not found.");

            var factor = await _catalogRepository.GetConversionFactorAsync(request.ProductId, request.UnitOfMeasureId)
                ?? throw new InvalidAssignmentException("This product has no conversion for the unit given.");

            var changeInBaseUnits = Math.Round(request.QuantityChange * factor, 4, MidpointRounding.AwayFromZero);
            if (changeInBaseUnits == 0)
                throw new InvalidAssignmentException("Quantity is too small — it rounds to zero in the product's base unit.");

            await _repository.AdjustStockAsync(request.ProductId, changeInBaseUnits, request.Reason.Trim(), _currentUser.UserId);
            return await GetStockLevelAsync(request.ProductId);
        }
    }
}
