using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Api.Auth;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Dashboard;
using UltimatePos.Application.Dashboard.Dtos;
using UltimatePos.Application.Reports;

namespace UltimatePos.Api.Controllers
{
    // At-a-glance numbers. Dashboard.View opens the door, but every section is also behind the permission that protects the same
    // data elsewhere (Sales.View, Costing.View, Customers.ViewLedger, Stock.View, Production.View): the section endpoints demand it,
    // and the summary leaves out, and never even queries, any section the caller lacks. A user with Dashboard.View and nothing
    // else gets 403, not an empty page.
    // Dates are calendar days (yyyy-MM-dd), both ends inclusive; leave both out for today, or give one for a single day.
    // utcOffsetMinutes is the business's local offset from UTC (default 180 = East Africa Time).
    [ApiController]
    [Authorize(Policy = "PERMISSION:Dashboard.View")]
    [Route("api/v1/dashboard")]
    public class DashboardController : ControllerBase
    {
        private readonly DashboardService _dashboardService;
        private readonly IAuthorizationService _authorization;

        public DashboardController(DashboardService dashboardService, IAuthorizationService authorization)
        {
            _dashboardService = dashboardService;
            _authorization = authorization;
        }

        // Every section the caller may see. Sections they may not see come back null and are named in hiddenSections.
        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary([FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
            [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes)
        {
            var allowed = await ResolveAllowedSectionsAsync();
            if (!DashboardPermissions.HasAnySection(allowed))
                return Forbid();

            var result = await _dashboardService.GetSummaryAsync(from, to, utcOffsetMinutes, allowed);
            return Ok(ApiResponse<DashboardSummaryDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Sales.View")]
        [HttpGet("sales")]
        public async Task<IActionResult> GetSales([FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
            [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes)
        {
            var result = await _dashboardService.GetSalesAsync(from, to, utcOffsetMinutes);
            return Ok(ApiResponse<DashboardSalesDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Costing.View")]
        [HttpGet("profit")]
        public async Task<IActionResult> GetProfit([FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
            [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes)
        {
            var result = await _dashboardService.GetProfitAsync(from, to, utcOffsetMinutes);
            return Ok(ApiResponse<DashboardProfitDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Customers.ViewLedger")]
        [HttpGet("receivables")]
        public async Task<IActionResult> GetReceivables()
        {
            var result = await _dashboardService.GetReceivablesAsync();
            return Ok(ApiResponse<DashboardReceivablesDto>.Ok(result));
        }

        // Low and out-of-stock counts need Stock.View. The stock value fields are cost data: null unless the caller also has Costing.View.
        [Authorize(Policy = "PERMISSION:Stock.View")]
        [HttpGet("stock")]
        public async Task<IActionResult> GetStock()
        {
            var includeValue = await _authorization.HasPermissionAsync(User, DashboardPermissions.PermissionFor(DashboardSections.StockValue));
            var result = await _dashboardService.GetStockAsync(includeValue);
            return Ok(ApiResponse<DashboardStockDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Production.View")]
        [HttpGet("production")]
        public async Task<IActionResult> GetProduction([FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
            [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes)
        {
            var result = await _dashboardService.GetProductionAsync(from, to, utcOffsetMinutes);
            return Ok(ApiResponse<DashboardProductionDto>.Ok(result));
        }

        private async Task<DashboardSections> ResolveAllowedSectionsAsync()
        {
            var allowed = DashboardSections.None;
            foreach (var (section, permission) in DashboardPermissions.Required)
            {
                if (await _authorization.HasPermissionAsync(User, permission))
                    allowed |= section;
            }

            // The value is a detail of the Stock section; without Stock.View there is nothing for it to be part of.
            if (!allowed.HasFlag(DashboardSections.Stock))
                allowed &= ~DashboardSections.StockValue;

            return allowed;
        }
    }
}
