using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Inventory.Dtos;

namespace UltimatePos.Api.Controllers
{
    [ApiController]
    [Route("api/v1/stock")]
    public class StockController : ControllerBase
    {
        private readonly InventoryService _inventoryService;
        public StockController(InventoryService inventoryService) => _inventoryService = inventoryService;

        [Authorize(Policy = "PERMISSION:Stock.View")]
        [HttpGet]
        public async Task<IActionResult> GetStockLevels([FromQuery] bool belowReorderOnly = false)
        {
            var result = await _inventoryService.GetStockLevelsAsync(belowReorderOnly);
            return Ok(ApiResponse<IEnumerable<StockLevelDto>>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Stock.View")]
        [HttpGet("{productId:guid}")]
        public async Task<IActionResult> GetStockLevel(Guid productId)
        {
            var result = await _inventoryService.GetStockLevelAsync(productId);
            return Ok(ApiResponse<StockLevelDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Stock.View")]
        [HttpGet("{productId:guid}/movements")]
        public async Task<IActionResult> GetMovements(Guid productId)
        {
            var result = await _inventoryService.GetStockMovementsAsync(productId);
            return Ok(ApiResponse<IEnumerable<StockMovementDto>>.Ok(result));
        }
    }
}
