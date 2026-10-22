using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Accounting;
using UltimatePos.Application.Accounting.Dtos;
using UltimatePos.Application.Business;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Customers;
using UltimatePos.Application.Reports;

namespace UltimatePos.Api.Controllers;

[ApiController]
[Route("api/v1/customers")]
public class CustomersController : ControllerBase
{
    private readonly BusinessService _businessService;
    private readonly AccountingService _accountingService;
    private readonly CustomerInsightsService _insights;
    public CustomersController(BusinessService businessService, AccountingService accountingService, CustomerInsightsService insights) {
        _businessService = businessService;
        _accountingService = accountingService;
        _insights = insights;
    }

    /// <summary>
    /// Customers who bought in the period (from/to: local days; default the last 90), with how often, how much and how recently, a
    /// segment relative to the others, and a suggestion (consider a discount, win back, review a discount). Ranked by spend by default.
    /// </summary>
    [Authorize(Policy = "PERMISSION:Customers.ViewInsights")]
    [HttpGet("insights")]
    public async Task<IActionResult> GetInsights([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] CustomerSegment? segment = null,
        [FromQuery] CustomerSuggestedAction? suggestedAction = null, [FromQuery] string? search = null,
        [FromQuery] CustomerInsightsSort sort = CustomerInsightsSort.Spend, [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes)
    {
        var paged = await _insights.GetCustomersAsync(page, pageSize,
            new CustomerInsightsQuery(from, to, segment, suggestedAction, search, sort, utcOffsetMinutes));
        var meta = new PagingMeta(paged.Page, paged.PageSize, paged.TotalCount, paged.TotalPages);
        return Ok(ApiResponse<IEnumerable<CustomerBuyingSummaryDto>>.Ok(paged.Items, meta));
    }

    /// <summary>One customer's buying (default the last 365 days): by month, top products, payment mix, segment and the reasons for it.</summary>
    [Authorize(Policy = "PERMISSION:Customers.ViewInsights")]
    [HttpGet("{id:guid}/insights")]
    public async Task<IActionResult> GetCustomerInsights(Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes)
    {
        var result = await _insights.GetCustomerAsync(id, from, to, utcOffsetMinutes);
        return Ok(ApiResponse<CustomerInsightsDto>.Ok(result));
    }

    /// <summary>Gives, changes or (null / 0) removes the customer's standing discount, applied to every sale to them from now on.</summary>
    [Authorize(Policy = "PERMISSION:Customers.ManageDiscount")]
    [HttpPut("{id:guid}/discount")]
    public async Task<IActionResult> SetDiscount(Guid id, SetCustomerDiscountRequestDto request)
    {
        var result = await _insights.SetDiscountAsync(id, request);
        return Ok(ApiResponse<CustomerDto>.Ok(result));
    }




    [Authorize(Policy = "PERMISSION:Customers.Register")]
    [HttpPost]
    public async Task<IActionResult> Register(RegisterCustomerRequestDto request)
    {
        var result = await _businessService.RegisterCustomerAsync(request);
        return Ok(ApiResponse<CustomerDto>.Ok(result));
    }

    // Always the customers of the business signed in to. businessId is accepted for older clients and must match it.
    [Authorize(Policy = "PERMISSION:Customers.View")]
    [HttpGet]
    public async Task<IActionResult> GetCustomers([FromQuery] Guid? businessId = null, [FromQuery] string? search = null)
    {
        var result = await _businessService.GetCustomersAsync(businessId, search);
        return Ok(ApiResponse<IEnumerable<CustomerDto>>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Customers.View")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetCustomer(Guid id)
    {
        var result = await _businessService.GetCustomerByIdAsync(id);
        return Ok(ApiResponse<CustomerDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Customers.Update")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateCustomer(Guid id, UpdateCustomerRequestDto request)
    {
        var result = await _businessService.UpdateCustomerAsync(id, request);
        return Ok(ApiResponse<CustomerDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Customers.Deactivate")]
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        var result = await _businessService.SetCustomerActiveStatusAsync(id, true);
        return Ok(ApiResponse<CustomerDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Customers.Deactivate")]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var result = await _businessService.SetCustomerActiveStatusAsync(id, false);
        return Ok(ApiResponse<CustomerDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Customers.ViewLedger")]
    [HttpGet("{id:guid}/balance")]
    public async Task<IActionResult> GetBalance(Guid id)
    {
        var result = await _accountingService.GetCustomerBalanceAsync(id);
        return Ok(ApiResponse<CustomerBalanceDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Customers.ViewLedger")]
    [HttpGet("{id:guid}/ledger")]
    public async Task<IActionResult> GetLedger(Guid id)
    {
        var result = await _accountingService.GetCustomerLedgerAsync(id);
        return Ok(ApiResponse<IEnumerable<CustomerLedgerEntryDto>>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Customers.RecordPayment")]
    [HttpPost("{id:guid}/payments")]
    public async Task<IActionResult> RecordPayment(Guid id, RecordCustomerPaymentRequestDto request)
    {
        var result = await _accountingService.RecordCustomerPaymentAsync(id, request);
        return Ok(ApiResponse<CustomerBalanceDto>.Ok(result));
    }
}