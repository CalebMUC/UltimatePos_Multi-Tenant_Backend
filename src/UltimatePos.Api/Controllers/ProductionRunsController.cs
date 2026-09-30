using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Production;
using UltimatePos.Application.Production.Dtos;

namespace UltimatePos.Api.Controllers
{
    [ApiController]
    [Route("api/v1/production-runs")]
    public class ProductionRunsController : ControllerBase
    {
        private readonly ProductionService _productionService;
        public ProductionRunsController(ProductionService productionService) => _productionService = productionService;

        // Read-only: what this batch would consume, what's on hand, what's short. Same calculation the real run uses.
        [Authorize(Policy = "PERMISSION:Production.View")]
        [HttpPost("preview")]
        public async Task<IActionResult> Preview(CreateProductionRunRequestDto request)
        {
            var result = await _productionService.PreviewProductionRunAsync(request);
            return Ok(ApiResponse<ProductionPreviewDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Production.Run")]
        [HttpPost]
        public async Task<IActionResult> Record(CreateProductionRunRequestDto request)
        {
            var result = await _productionService.RecordProductionRunAsync(request);
            return Ok(ApiResponse<ProductionRunDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Production.View")]
        [HttpGet]
        public async Task<IActionResult> GetRuns(
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? productId = null)
        {
            var paged = await _productionService.GetProductionRunsAsync(page, pageSize, productId);
            var meta = new PagingMeta(paged.Page, paged.PageSize, paged.TotalCount, paged.TotalPages);
            return Ok(ApiResponse<IEnumerable<ProductionRunDto>>.Ok(paged.Items, meta));
        }

        [Authorize(Policy = "PERMISSION:Production.View")]
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetRun(Guid id)
        {
            var result = await _productionService.GetProductionRunByIdAsync(id);
            return Ok(ApiResponse<ProductionRunDto>.Ok(result));
        }
    }
}
