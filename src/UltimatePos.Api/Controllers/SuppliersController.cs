using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Accounting;
using UltimatePos.Application.Accounting.Dtos;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Purchasing;
using UltimatePos.Application.Purchasing.Dtos;

namespace UltimatePos.Api.Controllers
{
    [ApiController]
    [Route("api/v1/suppliers")]
    public class SuppliersController : ControllerBase
    {
        private readonly PurchasingService _purchasingService;
        private readonly AccountingService _accountingService;
        public SuppliersController(PurchasingService purchasingService, AccountingService accountingService)
        {
            _purchasingService = purchasingService;
            _accountingService = accountingService;
        }

        [Authorize(Policy = "PERMISSION:Suppliers.Register")]
        [HttpPost]
        public async Task<IActionResult> Register(CreateSupplierRequestDto request)
        {
            var result = await _purchasingService.RegisterSupplierAsync(request);
            return Ok(ApiResponse<SupplierDto>.Ok(result));
        }

        // Always the suppliers of the business signed in to. businessId is accepted for older clients and must match it.
        [Authorize(Policy = "PERMISSION:Suppliers.View")]
        [HttpGet]
        public async Task<IActionResult> GetSuppliers([FromQuery] Guid? businessId = null)
        {
            var result = await _purchasingService.GetSuppliersAsync(businessId);
            return Ok(ApiResponse<IEnumerable<SupplierDto>>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Suppliers.View")]
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetSupplier(Guid id)
        {
            var result = await _purchasingService.GetSupplierByIdAsync(id);
            return Ok(ApiResponse<SupplierDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Suppliers.Update")]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> UpdateSupplier(Guid id, UpdateSupplierRequestDto request)
        {
            var result = await _purchasingService.UpdateSupplierAsync(id, request);
            return Ok(ApiResponse<SupplierDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Suppliers.Deactivate")]
        [HttpPost("{id:guid}/activate")]
        public async Task<IActionResult> Activate(Guid id)
        {
            var result = await _purchasingService.SetSupplierActiveStatusAsync(id, true);
            return Ok(ApiResponse<SupplierDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Suppliers.Deactivate")]
        [HttpPost("{id:guid}/deactivate")]
        public async Task<IActionResult> Deactivate(Guid id)
        {
            var result = await _purchasingService.SetSupplierActiveStatusAsync(id, false);
            return Ok(ApiResponse<SupplierDto>.Ok(result));
        }
    
        // ---- Payables: what the business owes each supplier ----

        /// <summary>Every supplier with a balance, largest first. Receiving goods posts a bill; payments reduce it.</summary>
        [Authorize(Policy = "PERMISSION:Suppliers.ViewLedger")]
        [HttpGet("balances")]
        public async Task<IActionResult> GetBalances()
        {
            var result = await _accountingService.GetSupplierBalancesAsync();
            return Ok(ApiResponse<IEnumerable<SupplierBalanceDto>>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Suppliers.ViewLedger")]
        [HttpGet("{id:guid}/balance")]
        public async Task<IActionResult> GetBalance(Guid id)
        {
            var result = await _accountingService.GetSupplierBalanceAsync(id);
            return Ok(ApiResponse<SupplierBalanceDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Suppliers.ViewLedger")]
        [HttpGet("{id:guid}/ledger")]
        public async Task<IActionResult> GetLedger(Guid id)
        {
            var result = await _accountingService.GetSupplierLedgerAsync(id);
            return Ok(ApiResponse<IEnumerable<SupplierLedgerEntryDto>>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Suppliers.RecordPayment")]
        [HttpPost("{id:guid}/payments")]
        public async Task<IActionResult> RecordPayment(Guid id, RecordSupplierPaymentRequestDto request)
        {
            var result = await _accountingService.RecordSupplierPaymentAsync(id, request);
            return Ok(ApiResponse<SupplierBalanceDto>.Ok(result));
        }
}
}
