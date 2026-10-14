using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Expenses
{
    /// <summary>Dates are inclusive on both ends. MissingEtims: true = only expenses with no eTIMS control number, false = only those with one.</summary>
    public record ExpenseFilter(DateOnly? From, DateOnly? To, Guid? ExpenseCategoryId, Guid? SupplierId,
        ExpensePaymentMethod? PaymentMethod, ExpenseStatus? Status, string? Search, bool? MissingEtims);

    /// <summary>One posted expense reduced to what summaries and the profit-and-loss report need.</summary>
    public record ExpenseSummaryRow(Guid ExpenseCategoryId, string CategoryCode, string CategoryName,
        ExpensePaymentMethod PaymentMethod, decimal Amount, bool HasEtimsInvoice);

    public interface IExpenseRepository
    {
        // ---- Categories ----
        Task<ExpenseCategory?> GetCategoryByIdAsync(Guid expenseCategoryId);
        Task<IEnumerable<ExpenseCategory>> GetCategoriesAsync(bool activeOnly);
        Task<bool> CategoryCodeExistsAsync(string code);
        Task<bool> CategoryNameExistsAsync(string name, Guid? excludingCategoryId);
        Task<ExpenseCategory> CreateCategoryAsync(ExpenseCategory category);
        Task UpdateCategoryAsync(Guid expenseCategoryId, string name, string? description, Guid? updatedBy);
        Task SetCategoryActiveStatusAsync(Guid expenseCategoryId, bool isActive, Guid? updatedBy);

        // ---- Payee lookup (kept here so Expenses does not depend on the whole Purchasing repository) ----
        Task<Supplier?> GetSupplierByIdAsync(Guid supplierId);

        // ---- Expenses ----
        Task<int> GetNextDocumentNumberAsync(string prefix);
        Task<Expense> CreateExpenseAsync(Expense expense);
        /// <summary>Includes Category and Supplier.</summary>
        Task<Expense?> GetExpenseByIdAsync(Guid expenseId);
        Task<(IEnumerable<Expense> Items, int TotalCount)> GetExpensesAsync(ExpenseFilter filter, int page, int pageSize);
        Task UpdateExpenseAsync(Guid expenseId, Guid expenseCategoryId, string description, string? paymentReference, Guid? supplierId,
            string? payeeName, string? supplierInvoiceNumber, string? etimsControlNumber, string? receiptUrl, string? notes, Guid? updatedBy);
        Task VoidExpenseAsync(Guid expenseId, string reason, Guid? voidedBy);

        // ---- Duplicate-entry guards (posted expenses only) ----
        Task<bool> MpesaReferenceExistsAsync(string reference, Guid? excludingExpenseId);
        Task<bool> SupplierInvoiceExistsAsync(Guid supplierId, string invoiceNumber, Guid? excludingExpenseId);

        // ---- Reporting ----
        Task<IReadOnlyList<ExpenseSummaryRow>> GetSummaryRowsAsync(DateOnly from, DateOnly toInclusive);
    }
}