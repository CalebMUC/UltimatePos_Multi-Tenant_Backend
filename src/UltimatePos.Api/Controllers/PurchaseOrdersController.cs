using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Purchasing;
using UltimatePos.Application.Purchasing.Dtos;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Api.Controllers
{
    [ApiController]
    [Route("api/v1/purchase-orders")]
    public class PurchaseOrdersController : ControllerBase
    {
        private readonly PurchasingService _purchasingService;
        public PurchaseOrdersController(PurchasingService purchasingService) => _purchasingService = purchasingService;

        [Authorize(Policy = "PERMISSION:PurchaseOrders.Create")]
        [HttpPost]
        public async Task<IActionResult> Create(CreatePurchaseOrderRequestDto request)
        {
            var result = await _purchasingService.CreatePurchaseOrderAsync(request);
            return Ok(ApiResponse<PurchaseOrderDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:PurchaseOrders.Create")]
        [HttpGet("purchasable-item-types")]
        public async Task<IActionResult> GetPurchasableItemTypes([FromQuery] Guid businessId)
        {
            var result = await _purchasingService.GetPurchasableItemTypesAsync(businessId);
            return Ok(ApiResponse<IEnumerable<ItemType>>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:PurchaseOrders.View")]
        [HttpGet]
        public async Task<IActionResult> GetOrders(
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
            [FromQuery] Guid? supplierId = null, [FromQuery] PurchaseOrderStatus? status = null)
        {
            var paged = await _purchasingService.GetPurchaseOrdersAsync(page, pageSize, supplierId, status);
            var meta = new PagingMeta(paged.Page, paged.PageSize, paged.TotalCount, paged.TotalPages);
            return Ok(ApiResponse<IEnumerable<PurchaseOrderDto>>.Ok(paged.Items, meta));
        }

        [Authorize(Policy = "PERMISSION:PurchaseOrders.View")]
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetOrder(Guid id)
        {
            var result = await _purchasingService.GetPurchaseOrderByIdAsync(id);
            return Ok(ApiResponse<PurchaseOrderDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:PurchaseOrders.Cancel")]
        [HttpPost("{id:guid}/cancel")]
        public async Task<IActionResult> Cancel(Guid id)
        {
            var result = await _purchasingService.CancelPurchaseOrderAsync(id);
            return Ok(ApiResponse<PurchaseOrderDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:PurchaseOrders.Receive")]
        [HttpPost("{id:guid}/receive")]
        public async Task<IActionResult> Receive(Guid id, ReceivePurchaseOrderRequestDto request)
        {
            var result = await _purchasingService.ReceivePurchaseOrderAsync(id, request);
            return Ok(ApiResponse<PurchaseOrderDto>.Ok(result));
        }
    }
}
