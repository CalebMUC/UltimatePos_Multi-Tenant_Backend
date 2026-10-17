using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Payments;
using UltimatePos.Application.Sales;
using UltimatePos.Application.Sales.Dtos;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Api.Controllers
{
    [ApiController]
    [Route("api/v1/sales")]
    public class SalesController : ControllerBase
    {
        private readonly SalesService _salesService;
        private readonly MpesaService _mpesaService;

        public SalesController(SalesService salesService, MpesaService mpesaService)
        {
            _salesService = salesService;
            _mpesaService = mpesaService;
        }

        [Authorize(Policy = "PERMISSION:Sales.View")]
        [HttpPost("preview")]
        public async Task<IActionResult> Preview(CreateSaleRequestDto request)
        {
            var result = await _salesService.PreviewSaleAsync(request);
            return Ok(ApiResponse<SalePreviewDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Sales.Create")]
        [HttpPost]
        public async Task<IActionResult> Create(CreateSaleRequestDto request)
        {
            var result = await _salesService.RecordSaleAsync(request);
            return Ok(ApiResponse<SaleDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Sales.View")]
        [HttpGet]
        // from / to are local calendar days, both inclusive. search matches the sale number and customer name. createdBy = cashier.
        public async Task<IActionResult> GetSales(
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
            [FromQuery] Guid? customerId = null, [FromQuery] SaleStatus? status = null,
            [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] Guid? createdBy = null,
            [FromQuery] PaymentMethod? paymentMethod = null, [FromQuery] string? search = null,
            [FromQuery] int utcOffsetMinutes = 180)
        {
            var paged = await _salesService.GetSalesAsync(page, pageSize,
                new SaleQuery(customerId, status, from, to, createdBy, paymentMethod, search, utcOffsetMinutes));
            var meta = new PagingMeta(paged.Page, paged.PageSize, paged.TotalCount, paged.TotalPages);
            return Ok(ApiResponse<IEnumerable<SaleDto>>.Ok(paged.Items, meta));
        }

        [Authorize(Policy = "PERMISSION:Sales.View")]
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetSale(Guid id)
        {
            var result = await _salesService.GetSaleByIdAsync(id);
            return Ok(ApiResponse<SaleDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Sales.Void")]
        [HttpPost("{id:guid}/void")]
        public async Task<IActionResult> Void(Guid id)
        {
            var result = await _salesService.VoidSaleAsync(id);
            return Ok(ApiResponse<SaleDto>.Ok(result));
        }
    
        /// <summary>
        /// Asks Safaricom what happened to a Pending STK push whose callback never arrived, and settles the sale accordingly.
        /// </summary>
        [Authorize(Policy = "PERMISSION:Sales.Create")]
        [HttpPost("{id:guid}/mpesa/reconcile")]
        public async Task<IActionResult> ReconcileMpesa(Guid id)
        {
            var result = await _mpesaService.ReconcileStkPushAsync(id);
            return Ok(ApiResponse<SaleDto>.Ok(result));
        }

        // ---- Returns ----

        /// <summary>Returns some or all of a completed sale's goods, refunding in cash, M-Pesa or a credit note.</summary>
        [Authorize(Policy = "PERMISSION:Sales.Return")]
        [HttpPost("{id:guid}/returns")]
        public async Task<IActionResult> CreateReturn(Guid id, CreateSaleReturnRequestDto request)
        {
            var result = await _salesService.CreateReturnAsync(id, request);
            return Ok(ApiResponse<SaleReturnDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Sales.View")]
        [HttpGet("{id:guid}/returns")]
        public async Task<IActionResult> GetReturnsForSale(Guid id)
        {
            var result = await _salesService.GetReturnsForSaleAsync(id);
            return Ok(ApiResponse<IEnumerable<SaleReturnDto>>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Sales.View")]
        [HttpGet("returns")]
        public async Task<IActionResult> GetReturns([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
            [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] int utcOffsetMinutes = 180)
        {
            var paged = await _salesService.GetReturnsAsync(page, pageSize, from, to, utcOffsetMinutes);
            var meta = new PagingMeta(paged.Page, paged.PageSize, paged.TotalCount, paged.TotalPages);
            return Ok(ApiResponse<IEnumerable<SaleReturnDto>>.Ok(paged.Items, meta));
        }

        // ---- Prescription register ----

        /// <summary>The prescription book: every sale dispensed against a prescription in the period, newest first.</summary>
        [Authorize(Policy = "PERMISSION:Prescriptions.View")]
        [HttpGet("prescriptions")]
        public async Task<IActionResult> GetPrescriptionRegister([FromQuery] DateOnly from, [FromQuery] DateOnly to,
            [FromQuery] string? search = null, [FromQuery] int utcOffsetMinutes = 180)
        {
            var result = await _salesService.GetPrescriptionRegisterAsync(from, to, utcOffsetMinutes, search);
            return Ok(ApiResponse<IEnumerable<PrescriptionRegisterEntryDto>>.Ok(result));
        }
}
}
