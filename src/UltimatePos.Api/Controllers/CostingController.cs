using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Costing;
using UltimatePos.Application.Costing.Dtos;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Api.Controllers
{
    [ApiController]
    [Route("api/v1/costing")]
    public class CostingController : ControllerBase
    {
        private readonly CostingService _costingService;
        public CostingController(CostingService costingService) => _costingService = costingService;

        // What the stock on hand is worth, by item type. Flags products that have stock but no recorded cost.
        [Authorize(Policy = "PERMISSION:Costing.View")]
        [HttpGet("stock-valuation")]
        public async Task<IActionResult> GetStockValuation([FromQuery] ItemType? itemType)
        {
            var result = await _costingService.GetStockValuationAsync(itemType);
            return Ok(ApiResponse<StockValuationDto>.Ok(result));
        }

        // Current price list against current cost: is anything priced below (or too close to) what it costs us?
        [Authorize(Policy = "PERMISSION:Costing.View")]
        [HttpGet("margins")]
        public async Task<IActionResult> GetMargins([FromQuery] PriceType? priceType)
        {
            var result = await _costingService.GetMarginsAsync(priceType);
            return Ok(ApiResponse<IEnumerable<MarginRowDto>>.Ok(result));
        }

        // Revenue, cost of goods sold and gross profit per product. `from` inclusive, `to` exclusive, both as full
        // timestamps with offset — the Nairobi month is from=2026-10-01T00:00:00%2B03:00&to=2026-11-01T00:00:00%2B03:00
        // (the '+' must be URL-encoded as %2B, otherwise it reads as a space; 'Z' for UTC needs no encoding).
        [Authorize(Policy = "PERMISSION:Costing.View")]
        [HttpGet("profit")]
        public async Task<IActionResult> GetProfit([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, [FromQuery] Guid? productId)
        {
            var result = await _costingService.GetProfitAsync(from, to, productId);
            return Ok(ApiResponse<ProfitReportDto>.Ok(result));
        }

        // Revenue - cost of goods sold - operating expenses = net profit, for whole days from..to (both inclusive, yyyy-MM-dd).
        // utcOffsetMinutes is the business's local offset from UTC (default 180 = East Africa Time); it only affects which
        // sales fall on which day. Check `isComplete`: false means some revenue has no recorded cost, so net profit is understated.
        [Authorize(Policy = "PERMISSION:Costing.View")]
        [HttpGet("profit-and-loss")]
        public async Task<IActionResult> GetProfitAndLoss([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] int utcOffsetMinutes = 180)
        {
            var result = await _costingService.GetProfitAndLossAsync(from, to, utcOffsetMinutes);
            return Ok(ApiResponse<ProfitAndLossDto>.Ok(result));
        }

        // Sets the average cost of stock that already exists (onboarding, or correcting a wrong cost). Quantity is untouched.
        [Authorize(Policy = "PERMISSION:Costing.Revalue")]
        [HttpPost("revaluations")]
        public async Task<IActionResult> Revalue(RevalueStockRequestDto request)
        {
            var result = await _costingService.RevalueStockAsync(request);
            return Ok(ApiResponse<StockValuationRowDto>.Ok(result));
        }
    }
}