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

        [Authorize(Policy = "PERMISSION:Stock.Adjust")]
        [HttpPost("adjustments")]
        public async Task<IActionResult> Adjust(CreateStockAdjustmentRequestDto request)
        {
            var result = await _inventoryService.AdjustStockAsync(request);
            return Ok(ApiResponse<StockLevelDto>.Ok(result));
        }
    
        /// <summary>
        /// Batches of batch-tracked products, soonest expiry first. expiringWithinDays narrows to lots expiring that soon (expired
        /// included); includeExpired=false hides expired lots; includeEmpty shows lots that are used up.
        /// </summary>
        [Authorize(Policy = "PERMISSION:Stock.View")]
        [HttpGet("batches")]
        public async Task<IActionResult> GetBatches([FromQuery] Guid? productId = null, [FromQuery] int? expiringWithinDays = null,
            [FromQuery] bool includeExpired = true, [FromQuery] bool includeEmpty = false)
        {
            var result = await _inventoryService.GetBatchesAsync(productId, expiringWithinDays, includeExpired, includeEmpty);
            return Ok(ApiResponse<IEnumerable<StockBatchDto>>.Ok(result));
        }

        /// <summary>Stock expired or expiring within `withinDays` (default 90), with its value when the caller has Costing.View.</summary>
        [Authorize(Policy = "PERMISSION:InventoryReports.View")]
        [HttpGet("expiry-report")]
        public async Task<IActionResult> GetExpiryReport([FromQuery] int withinDays = InventoryService.DefaultNearExpiryDays)
        {
            var result = await _inventoryService.GetExpiryReportAsync(withinDays);
            return Ok(ApiResponse<ExpiryReportDto>.Ok(result));
        }
}
}
