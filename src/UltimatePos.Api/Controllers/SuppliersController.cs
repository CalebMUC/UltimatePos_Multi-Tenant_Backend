using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        public SuppliersController(PurchasingService purchasingService) => _purchasingService = purchasingService;

        [Authorize(Policy = "PERMISSION:Suppliers.Register")]
        [HttpPost]
        public async Task<IActionResult> Register(CreateSupplierRequestDto request)
        {
            var result = await _purchasingService.RegisterSupplierAsync(request);
            return Ok(ApiResponse<SupplierDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Suppliers.View")]
        [HttpGet]
        public async Task<IActionResult> GetSuppliers([FromQuery] Guid businessId)
        {
            var result = await _purchasingService.GetSuppliersByBusinessAsync(businessId);
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
    }
}
