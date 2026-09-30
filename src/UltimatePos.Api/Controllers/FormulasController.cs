using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Production;
using UltimatePos.Application.Production.Dtos;

namespace UltimatePos.Api.Controllers
{
    [ApiController]
    [Route("api/v1/formulas")]
    public class FormulasController : ControllerBase
    {
        private readonly ProductionService _productionService;
        public FormulasController(ProductionService productionService) => _productionService = productionService;

        // Creates a NEW VERSION for the product and retires the previously active one. Formulas are never edited in place.
        [Authorize(Policy = "PERMISSION:Formulas.Create")]
        [HttpPost]
        public async Task<IActionResult> Create(CreateFormulaRequestDto request)
        {
            var result = await _productionService.CreateFormulaAsync(request);
            return Ok(ApiResponse<FormulaDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Formulas.View")]
        [HttpGet]
        public async Task<IActionResult> GetFormulas([FromQuery] Guid? productId = null, [FromQuery] bool activeOnly = false)
        {
            var result = await _productionService.GetFormulasAsync(productId, activeOnly);
            return Ok(ApiResponse<IEnumerable<FormulaDto>>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Formulas.View")]
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetFormula(Guid id)
        {
            var result = await _productionService.GetFormulaByIdAsync(id);
            return Ok(ApiResponse<FormulaDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Formulas.Deactivate")]
        [HttpPost("{id:guid}/deactivate")]
        public async Task<IActionResult> Deactivate(Guid id)
        {
            var result = await _productionService.DeactivateFormulaAsync(id);
            return Ok(ApiResponse<FormulaDto>.Ok(result));
        }
    }
}
