using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Accounting;
using UltimatePos.Application.Accounting.Dtos;
using UltimatePos.Application.Business;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Common.Dtos;

namespace UltimatePos.Api.Controllers;

[ApiController]
[Route("api/v1/customers")]
public class CustomersController : ControllerBase
{
    private readonly BusinessService _businessService;
    private readonly AccountingService _accountingService;
    public CustomersController(BusinessService businessService, AccountingService accountingService) { 
        _businessService = businessService;
        _accountingService = accountingService;
    }




    [Authorize(Policy = "PERMISSION:Customers.Register")]
    [HttpPost]
    public async Task<IActionResult> Register(RegisterCustomerRequestDto request)
    {
        var result = await _businessService.RegisterCustomerAsync(request);
        return Ok(ApiResponse<CustomerDto>.Ok(result));
    }

    // Scoped by business on purpose — a wholesaler only ever sees its own customer book.
    [Authorize(Policy = "PERMISSION:Customers.View")]
    [HttpGet]
    public async Task<IActionResult> GetCustomers([FromQuery] Guid businessId)
    {
        var result = await _businessService.GetCustomersByBusinessAsync(businessId);
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