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

        public AccountingRepository(UltimatePosDbContext context, CustomerLedger customerLedger)
        {
            _context = context;
            _customerLedger = customerLedger;
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
    }
}
