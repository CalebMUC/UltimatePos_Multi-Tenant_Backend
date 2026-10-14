using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Expenses;
using UltimatePos.Application.Expenses.Dtos;

namespace UltimatePos.Api.Controllers
{
    [ApiController]
    [Route("api/v1/expense-categories")]
    public class ExpenseCategoriesController : ControllerBase
    {
        private readonly ExpenseService _expenseService;
        public ExpenseCategoriesController(ExpenseService expenseService) => _expenseService = expenseService;

        [Authorize(Policy = "PERMISSION:ExpenseCategories.Create")]
        [HttpPost]
        public async Task<IActionResult> Create(CreateExpenseCategoryRequestDto request)
        {
            var result = await _expenseService.CreateCategoryAsync(request);
            return Ok(ApiResponse<ExpenseCategoryDto>.Ok(result));
        }

        // activeOnly=true is what an expense-entry dropdown should use.
        [Authorize(Policy = "PERMISSION:ExpenseCategories.View")]
        [HttpGet]
        public async Task<IActionResult> GetCategories([FromQuery] bool activeOnly = false)
        {
            var result = await _expenseService.GetCategoriesAsync(activeOnly);
            return Ok(ApiResponse<IEnumerable<ExpenseCategoryDto>>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:ExpenseCategories.View")]
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetCategory(Guid id)
        {
            var result = await _expenseService.GetCategoryByIdAsync(id);
            return Ok(ApiResponse<ExpenseCategoryDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:ExpenseCategories.Update")]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, UpdateExpenseCategoryRequestDto request)
        {
            var result = await _expenseService.UpdateCategoryAsync(id, request);
            return Ok(ApiResponse<ExpenseCategoryDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:ExpenseCategories.Deactivate")]
        [HttpPost("{id:guid}/activate")]
        public async Task<IActionResult> Activate(Guid id)
        {
            var result = await _expenseService.SetCategoryActiveStatusAsync(id, true);
            return Ok(ApiResponse<ExpenseCategoryDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:ExpenseCategories.Deactivate")]
        [HttpPost("{id:guid}/deactivate")]
        public async Task<IActionResult> Deactivate(Guid id)
        {
            var result = await _expenseService.SetCategoryActiveStatusAsync(id, false);
            return Ok(ApiResponse<ExpenseCategoryDto>.Ok(result));
        }
    }
}