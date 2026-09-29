using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Inventory.Dtos;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Application.Inventory
{
    public class InventoryService
    {
        private readonly IInventoryRepository _repository;
        public InventoryService(IInventoryRepository repository) => _repository = repository;

        public async Task<IEnumerable<StockLevelDto>> GetStockLevelsAsync(bool belowReorderOnly) =>
            (await _repository.GetStockLevelsAsync(belowReorderOnly))
                .Select(s => new StockLevelDto(s.ProductId, s.Product.Name, s.Product.Sku, s.QuantityOnHand, s.Product.ReorderLevel, s.QuantityOnHand <= s.Product.ReorderLevel));

        public async Task<StockLevelDto> GetStockLevelAsync(Guid productId)
        {
            var level = await _repository.GetStockLevelAsync(productId)
                ?? throw new NotFoundException($"No stock record for product '{productId}' — nothing has been received against it yet.");
            return new StockLevelDto(level.ProductId, level.Product.Name, level.Product.Sku, level.QuantityOnHand, level.Product.ReorderLevel, level.QuantityOnHand <= level.Product.ReorderLevel);
        }

        public async Task<IEnumerable<StockMovementDto>> GetStockMovementsAsync(Guid productId) =>
            (await _repository.GetStockMovementsAsync(productId))
                .Select(m => new StockMovementDto(m.StockMovementId, m.ProductId, m.MovementType.ToString(), m.QuantityChange, m.ReferenceType, m.ReferenceId, m.BatchId, m.Notes, m.CreatedAt));
    }
}
