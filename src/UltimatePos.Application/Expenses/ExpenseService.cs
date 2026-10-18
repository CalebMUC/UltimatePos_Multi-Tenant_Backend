using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UltimatePos.Application.Notifications;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Expenses.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Expenses
{
    public class ExpenseService
    {
        private const int MaxRangeDays = 366;
        private static readonly Regex CodePattern = new("^[A-Z0-9-]{1,20}$", RegexOptions.Compiled);

        private readonly IExpenseRepository _repository;
        private readonly ICurrentUser _currentUser;

        private readonly ISystemEventRecorder _events;

        public ExpenseService(IExpenseRepository repository, ICurrentUser currentUser, ISystemEventRecorder events)
        {
            _events = events;
            _repository = repository;
            _currentUser = currentUser;
        }

        // =====================================================================
        // Categories
        // =====================================================================

        public async Task<ExpenseCategoryDto> CreateCategoryAsync(CreateExpenseCategoryRequestDto request)
        {
            var code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();
            if (!CodePattern.IsMatch(code))
                throw new InvalidAssignmentException("Code is required and may contain only letters, digits and hyphens (maximum 20 characters).");

            var name = RequireText(request.Name, "Name", 100);
            var description = OptionalText(request.Description, "Description", 300);

            if (await _repository.CategoryCodeExistsAsync(code))
                throw new InvalidAssignmentException($"Category code '{code}' already exists.");
            if (await _repository.CategoryNameExistsAsync(name, null))
                throw new InvalidAssignmentException($"A category named '{name}' already exists.");

            var created = await _repository.CreateCategoryAsync(new ExpenseCategory
            {
                Code = code,
                Name = name,
                Description = description,
                CreatedBy = _currentUser.UserId
            });
            return ToDto(created);
        }

        public async Task<ExpenseCategoryDto> UpdateCategoryAsync(Guid expenseCategoryId, UpdateExpenseCategoryRequestDto request)
        {
            await GetCategoryOrThrowAsync(expenseCategoryId);

            var name = RequireText(request.Name, "Name", 100);
            var description = OptionalText(request.Description, "Description", 300);

            if (await _repository.CategoryNameExistsAsync(name, expenseCategoryId))
                throw new InvalidAssignmentException($"A category named '{name}' already exists.");

            await _repository.UpdateCategoryAsync(expenseCategoryId, name, description, _currentUser.UserId);
            return ToDto(await GetCategoryOrThrowAsync(expenseCategoryId));
        }

        public async Task<ExpenseCategoryDto> SetCategoryActiveStatusAsync(Guid expenseCategoryId, bool isActive)
        {
            await GetCategoryOrThrowAsync(expenseCategoryId);
            await _repository.SetCategoryActiveStatusAsync(expenseCategoryId, isActive, _currentUser.UserId);
            return ToDto(await GetCategoryOrThrowAsync(expenseCategoryId));
        }

        public async Task<ExpenseCategoryDto> GetCategoryByIdAsync(Guid expenseCategoryId) =>
            ToDto(await GetCategoryOrThrowAsync(expenseCategoryId));

        public async Task<IEnumerable<ExpenseCategoryDto>> GetCategoriesAsync(bool activeOnly) =>
            (await _repository.GetCategoriesAsync(activeOnly)).Select(ToDto);

        // =====================================================================
        // Expenses
        // =====================================================================

        public async Task<ExpenseDto> RecordExpenseAsync(CreateExpenseRequestDto request)
        {
            // Every check happens before a document number is drawn, so rejected requests never burn an EXP- number.
            if (request.ExpenseDate < new DateOnly(2000, 1, 1))
                throw new InvalidAssignmentException("A valid expense date is required (yyyy-MM-dd).");
            if (request.ExpenseDate > DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)) // +1 day: the clock may already be tomorrow where the user is
                throw new InvalidAssignmentException("The expense date cannot be in the future.");

            var description = RequireText(request.Description, "Description", 500);
            ValidateAmount(request.Amount);
            if (!Enum.IsDefined(request.PaymentMethod))
                throw new InvalidAssignmentException("Unknown payment method.");

            var reference = NormalizeReference(request.PaymentMethod, request.PaymentReference);
            var details = ValidateDetails(request.PayeeName, request.SupplierInvoiceNumber, request.EtimsControlNumber, request.ReceiptUrl, request.Notes);

            var category = await GetCategoryOrThrowAsync(request.ExpenseCategoryId);
            if (!category.IsActive)
                throw new InvalidAssignmentException($"Category '{category.Name}' is deactivated.");

            var supplier = await ResolveSupplierAsync(request.SupplierId, requireActive: true);
            await EnsureNotDuplicateAsync(request.PaymentMethod, reference, supplier, details.SupplierInvoiceNumber, null);

            var number = await _repository.GetNextDocumentNumberAsync("EXP");
            var created = await _repository.CreateExpenseAsync(new Expense
            {
                ExpenseNumber = $"EXP-{number:D6}",
                ExpenseDate = request.ExpenseDate,
                ExpenseCategoryId = category.ExpenseCategoryId,
                Description = description,
                Amount = request.Amount,
                PaymentMethod = request.PaymentMethod,
                PaymentReference = reference,
                SupplierId = supplier?.SupplierId,
                PayeeName = details.PayeeName,
                SupplierInvoiceNumber = details.SupplierInvoiceNumber,
                EtimsControlNumber = details.EtimsControlNumber,
                ReceiptUrl = details.ReceiptUrl,
                Notes = details.Notes,
                Status = ExpenseStatus.Posted,
                CreatedBy = _currentUser.UserId
            });

            return await GetExpenseByIdAsync(created.ExpenseId);
        }

        public async Task<ExpenseDto> GetExpenseByIdAsync(Guid expenseId) =>
            ToDto(await GetExpenseOrThrowAsync(expenseId));

        public async Task<PagedResult<ExpenseDto>> GetExpensesAsync(int page, int pageSize, ExpenseFilter filter)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            if (filter.From.HasValue && filter.To.HasValue && filter.From > filter.To)
                throw new InvalidAssignmentException("'from' cannot be after 'to'.");

            var search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim();
            if (search is { Length: > 100 })
                throw new InvalidAssignmentException("Search text cannot exceed 100 characters.");

            var (items, totalCount) = await _repository.GetExpensesAsync(filter with { Search = search }, page, pageSize);
            return new PagedResult<ExpenseDto>(items.Select(ToDto), page, pageSize, totalCount);
        }

        /// <summary>
        /// Corrects the descriptive details of a posted expense. Amount, date and payment method are deliberately NOT editable:
        /// a wrong figure is fixed by voiding and re-entering, so the books keep a visible trail of what was changed and by whom.
        /// </summary>
        public async Task<ExpenseDto> UpdateExpenseAsync(Guid expenseId, UpdateExpenseRequestDto request)
        {
            var existing = await GetExpenseOrThrowAsync(expenseId);
            if (existing.Status == ExpenseStatus.Voided)
                throw new InvalidAssignmentException("A voided expense cannot be edited.");

            var description = RequireText(request.Description, "Description", 500);
            var reference = NormalizeReference(existing.PaymentMethod, request.PaymentReference);
            var details = ValidateDetails(request.PayeeName, request.SupplierInvoiceNumber, request.EtimsControlNumber, request.ReceiptUrl, request.Notes);

            // A category or supplier that was deactivated AFTER this expense was posted must not block fixing a typo —
            // only a change TO an inactive one is refused.
            var category = await GetCategoryOrThrowAsync(request.ExpenseCategoryId);
            if (!category.IsActive && category.ExpenseCategoryId != existing.ExpenseCategoryId)
                throw new InvalidAssignmentException($"Category '{category.Name}' is deactivated.");

            var supplier = await ResolveSupplierAsync(request.SupplierId, requireActive: request.SupplierId != existing.SupplierId);
            await EnsureNotDuplicateAsync(existing.PaymentMethod, reference, supplier, details.SupplierInvoiceNumber, expenseId);

            await _repository.UpdateExpenseAsync(expenseId, category.ExpenseCategoryId, description, reference, supplier?.SupplierId,
                details.PayeeName, details.SupplierInvoiceNumber, details.EtimsControlNumber, details.ReceiptUrl, details.Notes, _currentUser.UserId);

            return await GetExpenseByIdAsync(expenseId);
        }

        public async Task<ExpenseDto> VoidExpenseAsync(Guid expenseId, VoidExpenseRequestDto request)
        {
            var existing = await GetExpenseOrThrowAsync(expenseId);
            if (existing.Status == ExpenseStatus.Voided)
                throw new InvalidAssignmentException("This expense has already been voided.");

            var reason = RequireText(request.Reason, "A reason", 300);
            _events.Record(new SystemEventDraft(SystemEventTypes.ExpenseVoided,
                $"Expense {existing.ExpenseNumber} of {existing.Amount:0.00} ({existing.Description}) was voided: {reason}",
                EventSeverity.Warning, "Expense", expenseId, new { existing.ExpenseNumber, existing.Amount, reason }));
            await _repository.VoidExpenseAsync(expenseId, reason, _currentUser.UserId);
            return await GetExpenseByIdAsync(expenseId);
        }

        // =====================================================================
        // Summary
        // =====================================================================

        /// <param name="from">Inclusive.</param>
        /// <param name="to">Inclusive.</param>
        public async Task<ExpenseSummaryDto> GetSummaryAsync(DateOnly from, DateOnly to)
        {
            ValidateDateRange(from, to);

            var rows = await _repository.GetSummaryRowsAsync(from, to);

            var byMethod = rows.GroupBy(r => r.PaymentMethod)
                .Select(g => new ExpensePaymentMethodTotalDto(g.Key.ToString(), g.Count(), g.Sum(r => r.Amount)))
                .OrderByDescending(m => m.Total)
                .ToList();

            var missingEtims = rows.Where(r => !r.HasEtimsInvoice).ToList();

            return new ExpenseSummaryDto(from, to, rows.Sum(r => r.Amount), rows.Count, GroupByCategory(rows), byMethod,
                missingEtims.Count, missingEtims.Sum(r => r.Amount));
        }

        /// <summary>Totals per category, largest first, with each category's share of the whole. Also used by the profit-and-loss report.</summary>
        public static IReadOnlyList<ExpenseCategoryTotalDto> GroupByCategory(IEnumerable<ExpenseSummaryRow> rows)
        {
            var list = rows.ToList();
            var total = list.Sum(r => r.Amount);

            return list.GroupBy(r => (r.ExpenseCategoryId, r.CategoryCode, r.CategoryName))
                .Select(g =>
                {
                    var categoryTotal = g.Sum(r => r.Amount);
                    var percent = total == 0 ? 0m : Math.Round(categoryTotal / total * 100m, 2, MidpointRounding.AwayFromZero);
                    return new ExpenseCategoryTotalDto(g.Key.ExpenseCategoryId, g.Key.CategoryCode, g.Key.CategoryName, g.Count(), categoryTotal, percent);
                })
                .OrderByDescending(c => c.Total).ThenBy(c => c.Name)
                .ToList();
        }

        public static void ValidateDateRange(DateOnly from, DateOnly to)
        {
            if (from > to)
                throw new InvalidAssignmentException("'from' cannot be after 'to'.");
            if (from.Year < 2000 || to.Year > 2200)
                throw new InvalidAssignmentException("Dates must be between the years 2000 and 2200.");
            if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
                throw new InvalidAssignmentException($"The range cannot exceed {MaxRangeDays} days — narrow it or run the report per period.");
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private async Task<ExpenseCategory> GetCategoryOrThrowAsync(Guid expenseCategoryId) =>
            await _repository.GetCategoryByIdAsync(expenseCategoryId)
                ?? throw new NotFoundException($"Expense category '{expenseCategoryId}' not found.");

        private async Task<Expense> GetExpenseOrThrowAsync(Guid expenseId) =>
            await _repository.GetExpenseByIdAsync(expenseId)
                ?? throw new NotFoundException($"Expense '{expenseId}' not found.");

        private async Task<Supplier?> ResolveSupplierAsync(Guid? supplierId, bool requireActive)
        {
            if (!supplierId.HasValue) return null;

            var supplier = await _repository.GetSupplierByIdAsync(supplierId.Value)
                ?? throw new NotFoundException($"Supplier '{supplierId}' not found.");
            if (requireActive && !supplier.IsActive)
                throw new InvalidAssignmentException($"Supplier '{supplier.SupplierName}' is deactivated.");
            return supplier;
        }

        // The same M-Pesa code twice, or the same supplier invoice twice, is almost always a double entry.
        // Bank and cheque references are NOT checked: one transfer can legitimately settle several expenses.
        private async Task EnsureNotDuplicateAsync(ExpensePaymentMethod method, string? reference, Supplier? supplier,
            string? supplierInvoiceNumber, Guid? excludingExpenseId)
        {
            if (method == ExpensePaymentMethod.Mpesa && reference is not null
                && await _repository.MpesaReferenceExistsAsync(reference, excludingExpenseId))
                throw new InvalidAssignmentException($"An expense with M-Pesa code '{reference}' is already recorded.");

            if (supplier is not null && supplierInvoiceNumber is not null
                && await _repository.SupplierInvoiceExistsAsync(supplier.SupplierId, supplierInvoiceNumber, excludingExpenseId))
                throw new InvalidAssignmentException($"Invoice '{supplierInvoiceNumber}' from {supplier.SupplierName} is already recorded.");
        }

        private static void ValidateAmount(decimal amount)
        {
            if (amount <= 0)
                throw new InvalidAssignmentException("Amount must be greater than zero.");
            if (decimal.Round(amount, 2) != amount)
                throw new InvalidAssignmentException("Amount cannot have more than 2 decimal places.");
        }

        private static string? NormalizeReference(ExpensePaymentMethod method, string? reference)
        {
            var value = OptionalText(reference, "Payment reference", 100);

            if (method != ExpensePaymentMethod.Cash && value is null)
                throw new InvalidAssignmentException(
                    $"A payment reference (M-Pesa code, bank reference or cheque number) is required for {method} payments.");

            return method == ExpensePaymentMethod.Mpesa ? value?.ToUpperInvariant() : value;
        }

        private sealed record Details(string? PayeeName, string? SupplierInvoiceNumber, string? EtimsControlNumber, string? ReceiptUrl, string? Notes);

        private static Details ValidateDetails(string? payeeName, string? invoiceNumber, string? etimsControlNumber, string? receiptUrl, string? notes)
        {
            var url = OptionalText(receiptUrl, "Receipt URL", 500);
            if (url is not null && !(Uri.TryCreate(url, UriKind.Absolute, out var uri)
                                     && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
                throw new InvalidAssignmentException("Receipt URL must be a valid http or https link.");

            return new Details(
                OptionalText(payeeName, "Payee name", 150),
                OptionalText(invoiceNumber, "Supplier invoice number", 100),
                OptionalText(etimsControlNumber, "eTIMS control number", 100),
                url,
                OptionalText(notes, "Notes", 1000));
        }

        private static string RequireText(string? value, string field, int maxLength)
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text))
                throw new InvalidAssignmentException($"{field} is required.");
            if (text.Length > maxLength)
                throw new InvalidAssignmentException($"{field} cannot exceed {maxLength} characters.");
            return text;
        }

        private static string? OptionalText(string? value, string field, int maxLength)
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text)) return null;
            if (text.Length > maxLength)
                throw new InvalidAssignmentException($"{field} cannot exceed {maxLength} characters.");
            return text;
        }

        // ---- Mapping ----

        private static ExpenseCategoryDto ToDto(ExpenseCategory c) =>
            new(c.ExpenseCategoryId, c.Code, c.Name, c.Description, c.IsActive);

        private static ExpenseDto ToDto(Expense e) =>
            new(e.ExpenseId, e.ExpenseNumber, e.ExpenseDate, e.ExpenseCategoryId, e.Category.Code, e.Category.Name, e.Description,
                e.Amount, e.PaymentMethod.ToString(), e.PaymentReference, e.SupplierId, e.Supplier?.SupplierName, e.PayeeName,
                e.SupplierInvoiceNumber, e.EtimsControlNumber, e.ReceiptUrl, e.Notes, e.Status.ToString(), e.CreatedAt,
                e.VoidedAt, e.VoidReason);
    }
}