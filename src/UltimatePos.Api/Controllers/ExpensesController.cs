using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Expenses;
using UltimatePos.Application.Expenses.Dtos;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Api.Controllers
{
    [ApiController]
    [Route("api/v1/expenses")]
    public class ExpensesController : ControllerBase
    {
        private readonly ExpenseService _expenseService;
        public ExpensesController(ExpenseService expenseService) => _expenseService = expenseService;

        [Authorize(Policy = "PERMISSION:Expenses.Create")]
        [HttpPost]
        public async Task<IActionResult> Record(CreateExpenseRequestDto request)
        {
            var result = await _expenseService.RecordExpenseAsync(request);
            return Ok(ApiResponse<ExpenseDto>.Ok(result));
        }

        // from / to are calendar dates (yyyy-MM-dd), both inclusive. missingEtims=true lists expenses with no eTIMS control number.
        [Authorize(Policy = "PERMISSION:Expenses.View")]
        [HttpGet]
        public async Task<IActionResult> GetExpenses(
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
            [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
            [FromQuery] Guid? categoryId = null, [FromQuery] Guid? supplierId = null,
            [FromQuery] ExpensePaymentMethod? paymentMethod = null, [FromQuery] ExpenseStatus? status = null,
            [FromQuery] string? search = null, [FromQuery] bool? missingEtims = null)
        {
            var filter = new ExpenseFilter(from, to, categoryId, supplierId, paymentMethod, status, search, missingEtims);
            var paged = await _expenseService.GetExpensesAsync(page, pageSize, filter);
            var meta = new PagingMeta(paged.Page, paged.PageSize, paged.TotalCount, paged.TotalPages);
            return Ok(ApiResponse<IEnumerable<ExpenseDto>>.Ok(paged.Items, meta));
        }

        // Totals by category and payment method for whole days from..to (both inclusive), plus how much is missing an eTIMS invoice.
        [Authorize(Policy = "PERMISSION:Expenses.View")]
        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary([FromQuery] DateOnly from, [FromQuery] DateOnly to)
        {
            var result = await _expenseService.GetSummaryAsync(from, to);
            return Ok(ApiResponse<ExpenseSummaryDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Expenses.View")]
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetExpense(Guid id)
        {
            var result = await _expenseService.GetExpenseByIdAsync(id);
            return Ok(ApiResponse<ExpenseDto>.Ok(result));
        }

        // Corrects descriptive details only. To change the amount, date or payment method, void the expense and record a new one.
        [Authorize(Policy = "PERMISSION:Expenses.Update")]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, UpdateExpenseRequestDto request)
        {
            var result = await _expenseService.UpdateExpenseAsync(id, request);
            return Ok(ApiResponse<ExpenseDto>.Ok(result));
        }

        [Authorize(Policy = "PERMISSION:Expenses.Void")]
        [HttpPost("{id:guid}/void")]
        public async Task<IActionResult> Void(Guid id, VoidExpenseRequestDto request)
        {
            var result = await _expenseService.VoidExpenseAsync(id, request);
            return Ok(ApiResponse<ExpenseDto>.Ok(result));
        }
    }
}