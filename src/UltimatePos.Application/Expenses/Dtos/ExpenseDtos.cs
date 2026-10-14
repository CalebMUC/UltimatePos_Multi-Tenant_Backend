using System;
using System.Collections.Generic;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Expenses.Dtos
{
    // ---- Categories ----
    public record CreateExpenseCategoryRequestDto(string Code, string Name, string? Description);
    public record UpdateExpenseCategoryRequestDto(string Name, string? Description);
    public record ExpenseCategoryDto(Guid ExpenseCategoryId, string Code, string Name, string? Description, bool IsActive);

    // ---- Expenses ----
    /// <param name="ExpenseDate">The calendar day the cost was incurred, as yyyy-MM-dd.</param>
    /// <param name="Amount">Total paid including any VAT, at most 2 decimal places.</param>
    /// <param name="PaymentReference">M-Pesa code, bank reference or cheque number. Required for every method except Cash.</param>
    /// <param name="EtimsControlNumber">Control number of the payee's eTIMS invoice, if they issued one.</param>
    public record CreateExpenseRequestDto(DateOnly ExpenseDate, Guid ExpenseCategoryId, string Description, decimal Amount,
        ExpensePaymentMethod PaymentMethod, string? PaymentReference, Guid? SupplierId, string? PayeeName,
        string? SupplierInvoiceNumber, string? EtimsControlNumber, string? ReceiptUrl, string? Notes);

    /// <summary>Date, amount and payment method cannot be changed on a posted expense — void it and enter a new one.</summary>
    public record UpdateExpenseRequestDto(Guid ExpenseCategoryId, string Description, string? PaymentReference, Guid? SupplierId,
        string? PayeeName, string? SupplierInvoiceNumber, string? EtimsControlNumber, string? ReceiptUrl, string? Notes);

    public record VoidExpenseRequestDto(string Reason);

    public record ExpenseDto(Guid ExpenseId, string ExpenseNumber, DateOnly ExpenseDate, Guid ExpenseCategoryId, string CategoryCode,
        string CategoryName, string Description, decimal Amount, string PaymentMethod, string? PaymentReference, Guid? SupplierId,
        string? SupplierName, string? PayeeName, string? SupplierInvoiceNumber, string? EtimsControlNumber, string? ReceiptUrl,
        string? Notes, string Status, DateTime CreatedAt, DateTime? VoidedAt, string? VoidReason);

    // ---- Summary (also used by the profit-and-loss report) ----
    public record ExpenseCategoryTotalDto(Guid ExpenseCategoryId, string Code, string Name, int ExpenseCount, decimal Total, decimal PercentOfTotal);
    public record ExpensePaymentMethodTotalDto(string PaymentMethod, int ExpenseCount, decimal Total);
    public record ExpenseSummaryDto(DateOnly From, DateOnly To, decimal TotalExpenses, int ExpenseCount,
        IEnumerable<ExpenseCategoryTotalDto> ByCategory, IEnumerable<ExpensePaymentMethodTotalDto> ByPaymentMethod,
        int ExpensesMissingEtimsCount, decimal ExpensesMissingEtimsAmount);
}