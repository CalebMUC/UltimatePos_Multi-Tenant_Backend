using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Accounting;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    public class AccountingRepository : IAccountingRepository
    {
        private readonly UltimatePosDbContext _context;
        private readonly CustomerLedger _customerLedger;
        private readonly SupplierLedger _supplierLedger;

        public AccountingRepository(UltimatePosDbContext context, CustomerLedger customerLedger, SupplierLedger supplierLedger)
        {
            _context = context;
            _customerLedger = customerLedger;
            _supplierLedger = supplierLedger;
        }

        public async Task<CustomerBalance?> GetCustomerBalanceAsync(Guid customerId) =>
            await _context.CustomerBalances.AsNoTracking().FirstOrDefaultAsync(b => b.CustomerId == customerId);

        public async Task<IEnumerable<CustomerLedgerEntry>> GetCustomerLedgerEntriesAsync(Guid customerId) =>
            await _context.CustomerLedgerEntries.AsNoTracking()
                .Where(e => e.CustomerId == customerId)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync();

        public async Task RecordPaymentAsync(CustomerLedgerEntry entry)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            await _customerLedger.ApplyAsync(entry);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }

        public async Task<SupplierBalance?> GetSupplierBalanceAsync(Guid supplierId) =>
            await _context.SupplierBalances.AsNoTracking().FirstOrDefaultAsync(b => b.SupplierId == supplierId);

        public async Task<IEnumerable<SupplierBalance>> GetSupplierBalancesAsync() =>
            await _context.SupplierBalances.AsNoTracking().Include(b => b.Supplier).ToListAsync();

        public async Task<IEnumerable<SupplierLedgerEntry>> GetSupplierLedgerEntriesAsync(Guid supplierId) =>
            await _context.SupplierLedgerEntries.AsNoTracking()
                .Where(e => e.SupplierId == supplierId)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync();

        public async Task RecordSupplierPaymentAsync(SupplierLedgerEntry entry)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            await _supplierLedger.ApplyAsync(entry);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }
    }
}
