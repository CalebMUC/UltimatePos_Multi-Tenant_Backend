using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UltimatePos.Application.Expenses;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    public class ExpenseRepository : IExpenseRepository
    {
        private readonly UltimatePosDbContext _context;
        public ExpenseRepository(UltimatePosDbContext context) => _context = context;

        // ---------------------------------------------------------------- Categories

        public async Task<ExpenseCategory?> GetCategoryByIdAsync(Guid expenseCategoryId) =>
            await _context.ExpenseCategories.AsNoTracking().FirstOrDefaultAsync(c => c.ExpenseCategoryId == expenseCategoryId);

        public async Task<IEnumerable<ExpenseCategory>> GetCategoriesAsync(bool activeOnly)
        {
            var query = _context.ExpenseCategories.AsNoTracking().AsQueryable();
            if (activeOnly)
                query = query.Where(c => c.IsActive);
            return await query.OrderBy(c => c.Name).ToListAsync();
        }

        // Codes are stored upper-case by the service, so an exact match is a case-insensitive match.
        public async Task<bool> CategoryCodeExistsAsync(string code) =>
            await _context.ExpenseCategories.AnyAsync(c => c.Code == code);

        public async Task<bool> CategoryNameExistsAsync(string name, Guid? excludingCategoryId)
        {
            var lowered = name.ToLower();
            return await _context.ExpenseCategories.AnyAsync(c =>
                c.Name.ToLower() == lowered && (!excludingCategoryId.HasValue || c.ExpenseCategoryId != excludingCategoryId.Value));
        }

        public async Task<ExpenseCategory> CreateCategoryAsync(ExpenseCategory category)
        {
            _context.ExpenseCategories.Add(category);
            await _context.SaveChangesAsync();
            return category;
        }

        public async Task UpdateCategoryAsync(Guid expenseCategoryId, string name, string? description, Guid? updatedBy)
        {
            var category = await _context.ExpenseCategories.FirstOrDefaultAsync(c => c.ExpenseCategoryId == expenseCategoryId)
                ?? throw new NotFoundException($"Expense category '{expenseCategoryId}' not found.");

            category.Name = name;
            category.Description = description;
            category.LastUpdatedAt = DateTime.UtcNow;
            category.LastUpdatedBy = updatedBy;
            await _context.SaveChangesAsync();
        }

        public async Task SetCategoryActiveStatusAsync(Guid expenseCategoryId, bool isActive, Guid? updatedBy)
        {
            var category = await _context.ExpenseCategories.FirstOrDefaultAsync(c => c.ExpenseCategoryId == expenseCategoryId)
                ?? throw new NotFoundException($"Expense category '{expenseCategoryId}' not found.");

            category.IsActive = isActive;
            category.LastUpdatedAt = DateTime.UtcNow;
            category.LastUpdatedBy = updatedBy;
            await _context.SaveChangesAsync();
        }

        // ---------------------------------------------------------------- Payees

        public async Task<Supplier?> GetSupplierByIdAsync(Guid supplierId) =>
            await _context.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.SupplierId == supplierId);

        // ---------------------------------------------------------------- Expenses

        // Same shared sequence table the other document numbers use (SKU, INV, PO, PR); the prefix keeps them apart.
        public async Task<int> GetNextDocumentNumberAsync(string prefix)
        {
            var results = await _context.Database
                .SqlQuery<int>($"""
                INSERT INTO "SkuSequences" ("Prefix", "LastNumber") VALUES ({prefix}, 1)
                ON CONFLICT ("Prefix") DO UPDATE SET "LastNumber" = "SkuSequences"."LastNumber" + 1
                RETURNING "LastNumber" AS "Value"
                """)
                .ToListAsync();
            return results.Single();
        }

        public async Task<Expense> CreateExpenseAsync(Expense expense)
        {
            _context.Expenses.Add(expense);
            await _context.SaveChangesAsync();
            return expense;
        }

        public async Task<Expense?> GetExpenseByIdAsync(Guid expenseId) =>
            await _context.Expenses.AsNoTracking()
                .Include(e => e.Category)
                .Include(e => e.Supplier)
                .FirstOrDefaultAsync(e => e.ExpenseId == expenseId);

        public async Task<(IEnumerable<Expense> Items, int TotalCount)> GetExpensesAsync(ExpenseFilter filter, int page, int pageSize)
        {
            var query = _context.Expenses.AsNoTracking()
                .Include(e => e.Category)
                .Include(e => e.Supplier)
                .AsQueryable();

            if (filter.From.HasValue)
                query = query.Where(e => e.ExpenseDate >= filter.From.Value);
            if (filter.To.HasValue)
                query = query.Where(e => e.ExpenseDate <= filter.To.Value);
            if (filter.ExpenseCategoryId.HasValue)
                query = query.Where(e => e.ExpenseCategoryId == filter.ExpenseCategoryId.Value);
            if (filter.SupplierId.HasValue)
                query = query.Where(e => e.SupplierId == filter.SupplierId.Value);
            if (filter.PaymentMethod.HasValue)
                query = query.Where(e => e.PaymentMethod == filter.PaymentMethod.Value);
            if (filter.Status.HasValue)
                query = query.Where(e => e.Status == filter.Status.Value);
            if (filter.MissingEtims == true)
                query = query.Where(e => e.EtimsControlNumber == null);
            else if (filter.MissingEtims == false)
                query = query.Where(e => e.EtimsControlNumber != null);

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var term = filter.Search.ToLower();
                query = query.Where(e =>
                    e.ExpenseNumber.ToLower().Contains(term)
                    || e.Description.ToLower().Contains(term)
                    || (e.PayeeName != null && e.PayeeName.ToLower().Contains(term))
                    || (e.PaymentReference != null && e.PaymentReference.ToLower().Contains(term))
                    || (e.SupplierInvoiceNumber != null && e.SupplierInvoiceNumber.ToLower().Contains(term)));
            }

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.CreatedAt)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        public async Task UpdateExpenseAsync(Guid expenseId, Guid expenseCategoryId, string description, string? paymentReference,
            Guid? supplierId, string? payeeName, string? supplierInvoiceNumber, string? etimsControlNumber, string? receiptUrl,
            string? notes, Guid? updatedBy)
        {
            var expense = await _context.Expenses.FirstOrDefaultAsync(e => e.ExpenseId == expenseId)
                ?? throw new NotFoundException($"Expense '{expenseId}' not found.");

            // Amount, date and payment method are intentionally not parameters: a posted figure is never rewritten.
            expense.ExpenseCategoryId = expenseCategoryId;
            expense.Description = description;
            expense.PaymentReference = paymentReference;
            expense.SupplierId = supplierId;
            expense.PayeeName = payeeName;
            expense.SupplierInvoiceNumber = supplierInvoiceNumber;
            expense.EtimsControlNumber = etimsControlNumber;
            expense.ReceiptUrl = receiptUrl;
            expense.Notes = notes;
            expense.LastUpdatedAt = DateTime.UtcNow;
            expense.LastUpdatedBy = updatedBy;
            await _context.SaveChangesAsync();
        }

        public async Task VoidExpenseAsync(Guid expenseId, string reason, Guid? voidedBy)
        {
            var expense = await _context.Expenses.FirstOrDefaultAsync(e => e.ExpenseId == expenseId)
                ?? throw new NotFoundException($"Expense '{expenseId}' not found.");

            expense.Status = ExpenseStatus.Voided;
            expense.VoidedAt = DateTime.UtcNow;
            expense.VoidedBy = voidedBy;
            expense.VoidReason = reason;
            await _context.SaveChangesAsync();
        }

        // ---------------------------------------------------------------- Duplicate guards (posted expenses only)

        public async Task<bool> MpesaReferenceExistsAsync(string reference, Guid? excludingExpenseId) =>
            await _context.Expenses.AnyAsync(e =>
                e.Status == ExpenseStatus.Posted && e.PaymentMethod == ExpensePaymentMethod.Mpesa && e.PaymentReference == reference
                && (!excludingExpenseId.HasValue || e.ExpenseId != excludingExpenseId.Value));

        public async Task<bool> SupplierInvoiceExistsAsync(Guid supplierId, string invoiceNumber, Guid? excludingExpenseId)
        {
            var lowered = invoiceNumber.ToLower();
            return await _context.Expenses.AnyAsync(e =>
                e.Status == ExpenseStatus.Posted && e.SupplierId == supplierId
                && e.SupplierInvoiceNumber != null && e.SupplierInvoiceNumber.ToLower() == lowered
                && (!excludingExpenseId.HasValue || e.ExpenseId != excludingExpenseId.Value));
        }

        // ---------------------------------------------------------------- Reporting

        public async Task<IReadOnlyList<ExpenseSummaryRow>> GetSummaryRowsAsync(DateOnly from, DateOnly toInclusive) =>
            await _context.Expenses.AsNoTracking()
                .Where(e => e.Status == ExpenseStatus.Posted && e.ExpenseDate >= from && e.ExpenseDate <= toInclusive)
                .Select(e => new ExpenseSummaryRow(e.ExpenseCategoryId, e.Category.Code, e.Category.Name, e.PaymentMethod, e.Amount, e.EtimsControlNumber != null))
                .ToListAsync();
    }
}