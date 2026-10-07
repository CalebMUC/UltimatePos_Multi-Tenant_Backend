using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Accounting.Dtos;
using UltimatePos.Application.Business;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Accounting
{
    public class AccountingService
    {
        private readonly IAccountingRepository _repository;
        private readonly IBusinessRepository _businessRepository;
        private readonly ICurrentUser _currentUser;

        public AccountingService(IAccountingRepository repository, IBusinessRepository businessRepository, ICurrentUser currentUser)
        {
            _repository = repository;
            _businessRepository = businessRepository;
            _currentUser = currentUser;
        }

        public async Task<CustomerBalanceDto> GetCustomerBalanceAsync(Guid customerId)
        {
            var customer = await _businessRepository.GetCustomerByIdAsync(customerId)
                ?? throw new NotFoundException($"Customer '{customerId}' not found.");

            var balance = await _repository.GetCustomerBalanceAsync(customerId);
            return new CustomerBalanceDto(customerId, customer.CustomerName, balance?.OutstandingBalance ?? 0m);
        }

        public async Task<IEnumerable<CustomerLedgerEntryDto>> GetCustomerLedgerAsync(Guid customerId)
        {
            if (await _businessRepository.GetCustomerByIdAsync(customerId) is null)
                throw new NotFoundException($"Customer '{customerId}' not found.");

            return (await _repository.GetCustomerLedgerEntriesAsync(customerId))
                .Select(e => new CustomerLedgerEntryDto(e.CustomerLedgerEntryId, e.EntryType, e.Amount, e.ReferenceType, e.ReferenceId, e.Notes, e.CreatedAt));
        }

        public async Task<CustomerBalanceDto> RecordCustomerPaymentAsync(Guid customerId, RecordCustomerPaymentRequestDto request)
        {
            if (request.Amount <= 0)
                throw new InvalidAssignmentException("Payment amount must be greater than zero.");

            if (await _businessRepository.GetCustomerByIdAsync(customerId) is null)
                throw new NotFoundException($"Customer '{customerId}' not found.");

            await _repository.RecordPaymentAsync(new CustomerLedgerEntry
            {
                CustomerId = customerId,
                EntryType = CustomerLedgerEntryType.Payment,
                Amount = request.Amount,
                ReferenceType = "CustomerPayment",
                ReferenceId = Guid.NewGuid(),
                Notes = request.Notes,
                CreatedBy = _currentUser.UserId
            });

            return await GetCustomerBalanceAsync(customerId);
        }
    }
}
