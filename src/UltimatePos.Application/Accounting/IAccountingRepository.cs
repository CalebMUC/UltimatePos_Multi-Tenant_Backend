using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Accounting
{
    public interface IAccountingRepository
    {
        Task<CustomerBalance?> GetCustomerBalanceAsync(Guid customerId);
        Task<IEnumerable<CustomerLedgerEntry>> GetCustomerLedgerEntriesAsync(Guid customerId);
        Task RecordPaymentAsync(CustomerLedgerEntry entry);

        Task<SupplierBalance?> GetSupplierBalanceAsync(Guid supplierId);
        Task<IEnumerable<SupplierBalance>> GetSupplierBalancesAsync();
        Task<IEnumerable<SupplierLedgerEntry>> GetSupplierLedgerEntriesAsync(Guid supplierId);
        Task RecordSupplierPaymentAsync(SupplierLedgerEntry entry);
    }
}
