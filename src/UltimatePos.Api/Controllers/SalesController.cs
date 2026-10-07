using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
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
        public SalesController(SalesService salesService) => _salesService = salesService;

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
        public async Task<IActionResult> GetSales(
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
            [FromQuery] Guid? customerId = null, [FromQuery] SaleStatus? status = null)
        {
            var paged = await _salesService.GetSalesAsync(page, pageSize, customerId, status);
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
    }
}
