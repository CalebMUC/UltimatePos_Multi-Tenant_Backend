using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Sales;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    public class SalesRepository : ISalesRepository
    {
        private readonly UltimatePosDbContext _context;
        private readonly StockLedger _stockLedger;
        private readonly CustomerLedger _customerLedger;

        public SalesRepository(UltimatePosDbContext context, StockLedger stockLedger, CustomerLedger customerLedger)
        {
            _context = context;
            _stockLedger = stockLedger;
            _customerLedger = customerLedger;
        }

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

        public async Task RecordCompletedSaleAsync(Sale sale, IEnumerable<StockMovement> movements, CustomerLedgerEntry? ledgerEntry)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            _context.Sales.Add(sale);

            foreach (var movement in movements.OrderBy(m => m.ProductId))
                await _stockLedger.ApplyAsync(movement, allowNegative: true);

            if (ledgerEntry is not null)
                await _customerLedger.ApplyAsync(ledgerEntry);

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }

        public async Task RecordPendingSaleAsync(Sale sale)
        {
            _context.Sales.Add(sale);
            await _context.SaveChangesAsync();
        }

        public async Task CompletePendingSaleAsync(Guid saleId, IEnumerable<StockMovement> movements, CustomerLedgerEntry? ledgerEntry, string? mpesaReceiptNumber)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.SaleId == saleId)
                ?? throw new NotFoundException($"Sale '{saleId}' not found.");

            sale.Status = SaleStatus.Completed;
            sale.LastUpdatedAt = DateTime.UtcNow;
            if (mpesaReceiptNumber is not null)
                sale.Notes = string.IsNullOrWhiteSpace(sale.Notes) ? $"M-Pesa receipt: {mpesaReceiptNumber}" : $"{sale.Notes} | M-Pesa receipt: {mpesaReceiptNumber}";

            foreach (var movement in movements.OrderBy(m => m.ProductId))
                await _stockLedger.ApplyAsync(movement, allowNegative: true);

            if (ledgerEntry is not null)
                await _customerLedger.ApplyAsync(ledgerEntry);

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }

        public async Task FailPendingSaleAsync(Guid saleId, string reason)
        {
            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.SaleId == saleId)
                ?? throw new NotFoundException($"Sale '{saleId}' not found.");

            sale.Status = SaleStatus.Failed;
            sale.Notes = string.IsNullOrWhiteSpace(sale.Notes) ? reason : $"{sale.Notes} | {reason}";
            sale.LastUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
        }

        public async Task<(IEnumerable<Sale> Items, int TotalCount)> GetSalesAsync(int page, int pageSize, Guid? customerId, SaleStatus? status)
        {
            var query = _context.Sales.AsNoTracking().Include(s => s.Lines).ThenInclude(l => l.Product).AsQueryable();

            if (customerId.HasValue)
                query = query.Where(s => s.CustomerId == customerId.Value);
            if (status.HasValue)
                query = query.Where(s => s.Status == status.Value);

            var totalCount = await query.CountAsync();
            var items = await query.OrderByDescending(s => s.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return (items, totalCount);
        }

        public async Task<Sale?> GetSaleByIdAsync(Guid saleId) =>
            await _context.Sales.AsNoTracking()
                .Include(s => s.Lines).ThenInclude(l => l.Product)
                .FirstOrDefaultAsync(s => s.SaleId == saleId);

        public async Task VoidSaleAsync(Guid saleId, IEnumerable<StockMovement> reversalMovements, CustomerLedgerEntry? reversalLedgerEntry, Guid? updatedBy)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.SaleId == saleId)
                ?? throw new NotFoundException($"Sale '{saleId}' not found.");

            sale.Status = SaleStatus.Voided;
            sale.LastUpdatedBy = updatedBy;
            sale.LastUpdatedAt = DateTime.UtcNow;

            foreach (var movement in reversalMovements.OrderBy(m => m.ProductId))
                await _stockLedger.ApplyAsync(movement, allowNegative: true);

            if (reversalLedgerEntry is not null)
                await _customerLedger.ApplyAsync(reversalLedgerEntry);

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }
    }
}
