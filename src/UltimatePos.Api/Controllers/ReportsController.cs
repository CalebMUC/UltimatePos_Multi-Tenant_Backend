using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Api.Auth;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Reports;
using UltimatePos.Application.Reports.Dtos;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Api.Controllers
{
    // Business-wide reports. Each report family has its OWN permission, so a role can be given the sales report without being
    // able to see what customers owe or what stock is worth: SalesReports.View, ProductionReports.View, CustomerReports.View,
    // InventoryReports.View and PurchaseReports.View. Every action carries its own [Authorize]: the fallback policy only requires
    // a login, so an action without one would be open to every user. The dashboard is in DashboardController.
    // Dates are calendar days (yyyy-MM-dd), both ends inclusive. utcOffsetMinutes is the business's local offset from UTC
    // (default 180 = East Africa Time) and only decides which records fall on which day.
    [ApiController]
    [Route("api/v1/reports")]
    public class ReportsController : ControllerBase
    {
        // What stock is WORTH is cost data, protected everywhere else by Costing.View. Inventory reports still give the quantities
        // without it, but every cost and value field comes back null.
        private const string CostPermission = "Costing.View";

        private readonly ReportsService _reportsService;
        private readonly IAuthorizationService _authorization;

        public ReportsController(ReportsService reportsService, IAuthorizationService authorization)
        {
            _reportsService = reportsService;
            _authorization = authorization;
        }

        // =====================================================================
        // Sales, production, customers
        // =====================================================================

        // Revenue by day, payment method, customer and product. `top` limits the customer and product lists (1..50, default 10).
        [Authorize(Policy = "PERMISSION:SalesReports.View")]
        [HttpGet("sales")]
        public async Task<IActionResult> GetSales([FromQuery] DateOnly from, [FromQuery] DateOnly to,
            [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes, [FromQuery] int top = ReportLimits.DefaultTop)
        {
            var result = await _reportsService.GetSalesReportAsync(from, to, utcOffsetMinutes, top);
            return Ok(ApiResponse<SalesReportDto>.Ok(result));
        }

        // What production actually cost: per product (and unit), the extra cost types, and the ingredients driving material cost.
        // `top` limits the ingredient list (1..50, default 10). Runs are dated by when they were recorded.
        [Authorize(Policy = "PERMISSION:ProductionReports.View")]
        [HttpGet("production-costs")]
        public async Task<IActionResult> GetProductionCosts([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? productId = null,
            [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes, [FromQuery] int top = ReportLimits.DefaultTop)
        {
            var result = await _reportsService.GetProductionCostReportAsync(from, to, utcOffsetMinutes, productId, top);
            return Ok(ApiResponse<ProductionCostReportDto>.Ok(result));
        }

        // How much each customer owes and how old it is: 0-30, 31-60, 61-90 and 90+ days from the invoice date. Payments settle the
        // oldest invoices first. asOf defaults to today. includeInvoices=true lists the open invoices behind each customer's figure;
        // includeSettled=true also lists customers who owe nothing.
        [Authorize(Policy = "PERMISSION:CustomerReports.View")]
        [HttpGet("customer-aging")]
        public async Task<IActionResult> GetCustomerAging([FromQuery] DateOnly? asOf = null, [FromQuery] Guid? customerId = null,
            [FromQuery] bool includeInvoices = false, [FromQuery] bool includeSettled = false,
            [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes)
        {
            var result = await _reportsService.GetCustomerAgingAsync(asOf, utcOffsetMinutes, customerId, includeInvoices, includeSettled);
            return Ok(ApiResponse<CustomerAgingReportDto>.Ok(result));
        }

        // =====================================================================
        // Inventory (InventoryReports.View; cost fields also need Costing.View)
        // =====================================================================

        // Where every product stands against its reorder level, most urgent first, with how many days the stock will last at the
        // recent rate of use (sales and production over the last `usageDays` complete days). status=LowStock or OutOfStock gives a
        // reorder list. Products that have never had stock are not listed; includeInactive=true adds deactivated products.
        [Authorize(Policy = "PERMISSION:InventoryReports.View")]
        [HttpGet("inventory/stock-status")]
        public async Task<IActionResult> GetStockStatus([FromQuery] ItemType? itemType = null, [FromQuery] Guid? categoryId = null,
            [FromQuery] StockStatus? status = null, [FromQuery] bool includeInactive = false,
            [FromQuery] int usageDays = ReportsService.DefaultUsageDays,
            [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes)
        {
            var includeCosts = await _authorization.HasPermissionAsync(User, CostPermission);
            var result = await _reportsService.GetStockStatusAsync(itemType, categoryId, status, includeInactive, usageDays, utcOffsetMinutes, includeCosts);
            return Ok(ApiResponse<StockStatusReportDto>.Ok(result));
        }

        // For each product that moved in the period: opening balance, received, produced, sold, consumed, adjusted in/out, closing.
        [Authorize(Policy = "PERMISSION:InventoryReports.View")]
        [HttpGet("inventory/movements")]
        public async Task<IActionResult> GetStockMovements([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? productId = null,
            [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes)
        {
            var includeCosts = await _authorization.HasPermissionAsync(User, CostPermission);
            var result = await _reportsService.GetMovementReportAsync(from, to, utcOffsetMinutes, productId, includeCosts);
            return Ok(ApiResponse<StockMovementReportDto>.Ok(result));
        }

        // Manual stock adjustments with who made them and why, plus what they cost. `rows` limits the list (1..500, default 100),
        // newest first; the totals always cover every adjustment in the period.
        [Authorize(Policy = "PERMISSION:InventoryReports.View")]
        [HttpGet("inventory/adjustments")]
        public async Task<IActionResult> GetStockAdjustments([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? productId = null,
            [FromQuery] int rows = ReportLimits.DefaultRows, [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes)
        {
            var includeCosts = await _authorization.HasPermissionAsync(User, CostPermission);
            var result = await _reportsService.GetAdjustmentReportAsync(from, to, utcOffsetMinutes, productId, rows, includeCosts);
            return Ok(ApiResponse<StockAdjustmentReportDto>.Ok(result));
        }

        // =====================================================================
        // Purchasing (PurchaseReports.View). Orders sit on the day they were raised.
        // =====================================================================

        // What was ordered, what has arrived and what is still to come, by status, supplier and product.
        // `top` limits the supplier and product lists (1..50, default 10).
        [Authorize(Policy = "PERMISSION:PurchaseReports.View")]
        [HttpGet("purchases/summary")]
        public async Task<IActionResult> GetPurchaseSummary([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? supplierId = null,
            [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes, [FromQuery] int top = ReportLimits.DefaultTop)
        {
            var result = await _reportsService.GetPurchaseSummaryAsync(from, to, utcOffsetMinutes, supplierId, top);
            return Ok(ApiResponse<PurchaseSummaryDto>.Ok(result));
        }

        // Orders still waiting on stock, overdue ones first, with what is outstanding on each. Always as of today.
        [Authorize(Policy = "PERMISSION:PurchaseReports.View")]
        [HttpGet("purchases/open-orders")]
        public async Task<IActionResult> GetOpenPurchaseOrders([FromQuery] Guid? supplierId = null,
            [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes)
        {
            var result = await _reportsService.GetOpenOrdersAsync(supplierId, utcOffsetMinutes);
            return Ok(ApiResponse<OpenOrdersReportDto>.Ok(result));
        }

        // What we paid per unit, per product, with the biggest price movements first. Give productId to see every individual
        // purchase (supplier, quantity, price) for that product. `rows` limits the list (1..500, default 100).
        [Authorize(Policy = "PERMISSION:PurchaseReports.View")]
        [HttpGet("purchases/price-trends")]
        public async Task<IActionResult> GetPurchasePriceTrends([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? productId = null,
            [FromQuery] Guid? supplierId = null, [FromQuery] int rows = ReportLimits.DefaultRows,
            [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes)
        {
            var result = await _reportsService.GetPriceTrendsAsync(from, to, utcOffsetMinutes, productId, supplierId, rows);
            return Ok(ApiResponse<PriceTrendsReportDto>.Ok(result));
        }
    }
}
