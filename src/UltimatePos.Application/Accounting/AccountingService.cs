using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Notifications;
using UltimatePos.Application.Accounting.Dtos;
using UltimatePos.Application.Business;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Purchasing;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Accounting
{
    public class AccountingService
    {
        private readonly IAccountingRepository _repository;
        private readonly IBusinessRepository _businessRepository;
        private readonly IPurchasingRepository _purchasingRepository;
        private readonly ICurrentUser _currentUser;

        private readonly ISystemEventRecorder _events;
        private readonly IBranchContext _branches;

        public AccountingService(IAccountingRepository repository, IBusinessRepository businessRepository,
            IPurchasingRepository purchasingRepository, ICurrentUser currentUser, ISystemEventRecorder events, IBranchContext branches)
        {
            _events = events;
            _branches = branches;
            _repository = repository;
            _businessRepository = businessRepository;
            _purchasingRepository = purchasingRepository;
            _currentUser = currentUser;
        }

        // ---- Customers (receivables) ----

        public async Task<CustomerBalanceDto> GetCustomerBalanceAsync(Guid customerId)
        {
            var customer = await _businessRepository.GetCustomerByIdAsync(customerId)
                ?? throw new NotFoundException($"Customer '{customerId}' not found.");

            var balance = await _repository.GetCustomerBalanceAsync(customerId);
            var outstanding = balance?.OutstandingBalance ?? 0m;
            return new CustomerBalanceDto(customerId, customer.CustomerName, outstanding, customer.CreditLimit,
                customer.CreditLimit is { } limit ? Math.Max(0m, limit - outstanding) : null);
        }

        public async Task<IEnumerable<CustomerLedgerEntryDto>> GetCustomerLedgerAsync(Guid customerId)
        {
            if (await _businessRepository.GetCustomerByIdAsync(customerId) is null)
                throw new NotFoundException($"Customer '{customerId}' not found.");

            return (await _repository.GetCustomerLedgerEntriesAsync(customerId))
                .Select(e => new CustomerLedgerEntryDto(e.CustomerLedgerEntryId, e.EntryType, e.Amount, e.ReferenceType, e.ReferenceId, e.Notes,
                    e.CreatedAt, e.PaymentMethod, e.PaymentReference));
        }

        public async Task<CustomerBalanceDto> RecordCustomerPaymentAsync(Guid customerId, RecordCustomerPaymentRequestDto request)
        {
            ValidatePayment(request.Amount, request.PaymentMethod, request.PaymentReference);

            var customer = await _businessRepository.GetCustomerByIdAsync(customerId)
                ?? throw new NotFoundException($"Customer '{customerId}' not found.");

            _events.Record(new SystemEventDraft(SystemEventTypes.CustomerPaymentRecorded,
                $"{customer.CustomerName} paid {request.Amount:0.00}{(request.PaymentMethod is { } m ? $" by {m}" : "")}.",
                EntityType: "Customer", EntityId: customerId, Data: new { request.Amount, request.PaymentMethod, request.PaymentReference }));
            await _repository.RecordPaymentAsync(new CustomerLedgerEntry
            {
                CustomerId = customerId,
                EntryType = CustomerLedgerEntryType.Payment,
                Amount = request.Amount,
                ReferenceType = "CustomerPayment",
                ReferenceId = Guid.NewGuid(),
                Notes = request.Notes,
                PaymentMethod = request.PaymentMethod,
                PaymentReference = string.IsNullOrWhiteSpace(request.PaymentReference) ? null : request.PaymentReference.Trim(),
                // Where the money was taken: a cash payment counts in that branch's cash-up.
                BranchId = _branches.ActiveBranchId,
                CreatedBy = _currentUser.UserId
            });

            return await GetCustomerBalanceAsync(customerId);
        }

        // ---- Suppliers (payables) ----

        public async Task<SupplierBalanceDto> GetSupplierBalanceAsync(Guid supplierId)
        {
            var supplier = await _purchasingRepository.GetSupplierByIdAsync(supplierId)
                ?? throw new NotFoundException($"Supplier '{supplierId}' not found.");
            var balance = await _repository.GetSupplierBalanceAsync(supplierId);
            return new SupplierBalanceDto(supplierId, supplier.SupplierName, balance?.OutstandingBalance ?? 0m);
        }

        /// <summary>Every supplier the business owes money to (or has paid ahead), largest first.</summary>
        public async Task<IEnumerable<SupplierBalanceDto>> GetSupplierBalancesAsync() =>
            (await _repository.GetSupplierBalancesAsync())
                .Where(b => b.OutstandingBalance != 0)
                .OrderByDescending(b => b.OutstandingBalance)
                .Select(b => new SupplierBalanceDto(b.SupplierId, b.Supplier.SupplierName, b.OutstandingBalance));

        public async Task<IEnumerable<SupplierLedgerEntryDto>> GetSupplierLedgerAsync(Guid supplierId)
        {
            if (await _purchasingRepository.GetSupplierByIdAsync(supplierId) is null)
                throw new NotFoundException($"Supplier '{supplierId}' not found.");

            return (await _repository.GetSupplierLedgerEntriesAsync(supplierId))
                .Select(e => new SupplierLedgerEntryDto(e.SupplierLedgerEntryId, e.EntryType, e.Amount, e.ReferenceType, e.ReferenceId,
                    e.SupplierInvoiceNumber, e.PaymentMethod, e.PaymentReference, e.Notes, e.CreatedAt));
        }

        public async Task<SupplierBalanceDto> RecordSupplierPaymentAsync(Guid supplierId, RecordSupplierPaymentRequestDto request)
        {
            ValidatePayment(request.Amount, request.PaymentMethod, request.PaymentReference);

            var supplier = await _purchasingRepository.GetSupplierByIdAsync(supplierId)
                ?? throw new NotFoundException($"Supplier '{supplierId}' not found.");

            _events.Record(new SystemEventDraft(SystemEventTypes.SupplierPaymentRecorded,
                $"Paid {supplier.SupplierName} {request.Amount:0.00} by {request.PaymentMethod}.",
                EntityType: "Supplier", EntityId: supplierId, Data: new { request.Amount, request.PaymentMethod, request.PaymentReference }));
            await _repository.RecordSupplierPaymentAsync(new SupplierLedgerEntry
            {
                SupplierId = supplierId,
                EntryType = SupplierLedgerEntryType.Payment,
                Amount = request.Amount,
                ReferenceType = "SupplierPayment",
                ReferenceId = Guid.NewGuid(),
                PaymentMethod = request.PaymentMethod,
                PaymentReference = string.IsNullOrWhiteSpace(request.PaymentReference) ? null : request.PaymentReference.Trim(),
                Notes = request.Notes,
                BranchId = _branches.ActiveBranchId,
                CreatedBy = _currentUser.UserId
            });

            return await GetSupplierBalanceAsync(supplierId);
        }

        private static void ValidatePayment(decimal amount, SettlementMethod? method, string? reference)
        {
            var errors = new Dictionary<string, string[]>();
            if (amount <= 0)
                errors["amount"] = new[] { "Payment amount must be greater than zero." };
            else if (decimal.Round(amount, 2) != amount)
                errors["amount"] = new[] { "Amount cannot have more than 2 decimal places." };
            if (method is { } m && !Enum.IsDefined(m))
                errors["paymentMethod"] = new[] { "Unknown payment method." };
            else if (method is { } given && given != SettlementMethod.Cash && string.IsNullOrWhiteSpace(reference))
                errors["paymentReference"] = new[] { "A reference (M-Pesa code, bank reference or cheque number) is required for anything but cash." };
            if (errors.Count > 0)
                throw new ValidationException(errors);
        }
    }
}
