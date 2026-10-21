using Microsoft.EntityFrameworkCore;
using Npgsql;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Sales;
using UltimatePos.Application.Sales.Dtos;
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
        private readonly IBranchContext _branches;

        public SalesRepository(UltimatePosDbContext context, StockLedger stockLedger, CustomerLedger customerLedger, IBranchContext branches)
        {
            _branches = branches;
            _context = context;
            _stockLedger = stockLedger;
            _customerLedger = customerLedger;
        }

        public async Task<int> GetNextDocumentNumberAsync(string prefix)
        {
            return await _context.NextSequenceNumberAsync(prefix);
        }

        public async Task RecordCompletedSaleAsync(Sale sale, CustomerLedgerEntry? ledgerEntry, DateOnly today, bool allowNegativeStock,
            Guid? tillTransactionId = null, Guid? deliveryOrderId = null)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            _context.Sales.Add(sale);
            await IssueLinesAsync(sale, today, strictBatches: true, allowNegativeStock);

            if (ledgerEntry is not null)
                await _customerLedger.ApplyAsync(ledgerEntry);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (sale.MpesaReference is not null
                && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: MpesaReferenceIndex })
            {
                // Another sale took the same typed code between the check and this save.
                await tx.RollbackAsync();
                var other = await GetSaleNumberByMpesaReferenceAsync(sale.MpesaReference);
                throw new DuplicateReferenceException($"M-Pesa code {sale.MpesaReference} is already on sale {other ?? "another sale"}.");
            }

            // Claimed in the same transaction, and only while still unmatched: two cashiers ringing up sales against one till payment
            // at the same moment get one sale, and the other is rolled back with its stock.
            if (tillTransactionId is { } transactionId)
            {
                var claimed = await _context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "MpesaTransactions"
                    SET "Status" = {MpesaTransactionStatus.Matched.ToString()}, "SaleId" = {sale.SaleId}, "LastUpdatedAt" = now()
                    WHERE "MpesaTransactionId" = {transactionId} AND "Status" = {MpesaTransactionStatus.Received.ToString()} AND "SaleId" IS NULL
                    """);
                if (claimed == 0)
                    throw new InvalidAssignmentException("This M-Pesa transaction has just been matched to another sale.");
            }

            if (deliveryOrderId is { } orderId)
            {
                var delivered = await _context.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "DeliveryOrders"
                    SET "Status" = {DeliveryOrderStatus.Delivered.ToString()}, "SaleId" = {sale.SaleId}, "DeliveredAt" = now(),
                        "DeliveredBy" = {sale.CreatedBy}, "LastUpdatedAt" = now(), "LastUpdatedBy" = {sale.CreatedBy}
                    WHERE "DeliveryOrderId" = {orderId} AND "BranchId" = {sale.BranchId}
                      AND "Status" = {DeliveryOrderStatus.OutForDelivery.ToString()}
                    """);
                if (delivered == 0)
                    throw new InvalidAssignmentException("This delivery has just been recorded (or cancelled) by someone else. Reload the order.");
            }

            await tx.CommitAsync();
        }

        private const string MpesaReferenceIndex = "IX_Sales_BusinessId_MpesaReference";

        public async Task<string?> GetSaleNumberByMpesaReferenceAsync(string mpesaReference) =>
            await _context.Sales.AsNoTracking()
                .Where(s => s.MpesaReference == mpesaReference && s.Status != SaleStatus.Voided && s.Status != SaleStatus.Failed)
                .Select(s => s.SaleNumber)
                .FirstOrDefaultAsync();

        public async Task RecordPendingSaleAsync(Sale sale)
        {
            _context.Sales.Add(sale);
            await _context.SaveChangesAsync();
        }

        public async Task CompletePendingSaleAsync(Guid saleId, CustomerLedgerEntry? ledgerEntry, string? mpesaReceiptNumber, DateOnly today)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            // A callback and a reconciliation (or two callbacks) can arrive together: lock the sale and settle it only while it is
            // still Pending, so its stock leaves once.
            await _context.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "Sales" WHERE "SaleId" = {saleId} FOR UPDATE""");
            var sale = await _context.Sales.Include(s => s.Lines).ThenInclude(l => l.BatchAllocations)
                           .FirstOrDefaultAsync(s => s.SaleId == saleId)
                     ?? throw new NotFoundException($"Sale '{saleId}' not found.");
            if (sale.Status != SaleStatus.Pending)
                return;

            sale.Status = SaleStatus.Completed;
            sale.LastUpdatedAt = DateTime.UtcNow;
            if (mpesaReceiptNumber is not null)
                AppendNote(sale, $"M-Pesa receipt: {mpesaReceiptNumber}");

            // Cost is fixed when stock actually leaves (at confirmation), not when the prompt was sent. The customer has already paid,
            // so stock is issued even if it has run out since the prompt was sent (a batch shortfall is noted on the sale; any product
            // driven to zero or below raises the usual out-of-stock alert).
            await IssueLinesAsync(sale, today, strictBatches: false, allowNegativeStock: true);

            if (ledgerEntry is not null)
                await _customerLedger.ApplyAsync(ledgerEntry);

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }

        /// <summary>
        /// Takes each line's stock out, in product order so concurrent sales lock rows in the same sequence. Batch-tracked products
        /// come out first-expiry-first-out; the cost each line left stock at is stamped on it. Other products may go negative only when
        /// <paramref name="allowNegativeStock"/>; otherwise the issue is guarded in SQL, so two sales at once cannot oversell between them.
        /// </summary>
        private async Task IssueLinesAsync(Sale sale, DateOnly today, bool strictBatches, bool allowNegativeStock)
        {
            // Stock leaves the branch the sale is rung up at (an M-Pesa confirmation has no signed-in branch: the sale carries it).
            if (sale.BranchId == Guid.Empty)
                sale.BranchId = _branches.RequireActiveBranchId();
            var productIds = sale.Lines.Select(l => l.ProductId).Distinct().ToList();
            var batchTracked = (await _context.Products.AsNoTracking()
                    .Where(p => productIds.Contains(p.ProductId) && p.IsBatchTracked)
                    .Select(p => p.ProductId).ToListAsync())
                .ToHashSet();

            foreach (var line in sale.Lines.OrderBy(l => l.ProductId))
            {
                var template = new StockMovement
                {
                    ProductId = line.ProductId,
                    BranchId = sale.BranchId,
                    MovementType = StockMovementType.Sale,
                    QuantityChange = -line.QuantityInBaseUnits,
                    ReferenceType = "Sale",
                    ReferenceId = sale.SaleId,
                    BatchId = sale.SaleId,
                    CreatedBy = sale.CreatedBy
                };

                if (!batchTracked.Contains(line.ProductId))
                {
                    await _stockLedger.ApplyAsync(template, allowNegative: allowNegativeStock);
                    line.UnitCost = template.UnitCost;
                    continue;
                }

                IReadOnlyList<(Guid StockBatchId, decimal Quantity, decimal? UnitCost)> issued;
                try
                {
                    issued = await _stockLedger.IssueFefoAsync(template, today);
                }
                catch (InsufficientStockException) when (!strictBatches)
                {
                    // The customer has already paid: issue what the lots hold and record the rest against the product total.
                    var available = await _context.StockBatches
                        .IgnoreQueryFilters()
                        .Where(b => b.ProductId == line.ProductId && b.BranchId == sale.BranchId && b.QuantityOnHand > 0
                                    && (b.ExpiryDate == null || b.ExpiryDate >= today))
                        .SumAsync(b => (decimal?)b.QuantityOnHand) ?? 0m;
                    var fromLots = Math.Min(available, line.QuantityInBaseUnits);
                    issued = fromLots > 0
                        ? await _stockLedger.IssueFefoAsync(Clone(template, -fromLots), today)
                        : Array.Empty<(Guid, decimal, decimal?)>();

                    var shortfall = Clone(template, -(line.QuantityInBaseUnits - fromLots));
                    shortfall.Notes = "Batch shortfall at payment confirmation — issued without a batch";
                    await _stockLedger.ApplyAsync(shortfall, allowNegative: true);
                    issued = issued.Count > 0 ? issued : new[] { (Guid.Empty, 0m, shortfall.UnitCost) };
                    AppendNote(sale, $"Batch shortfall of {line.QuantityInBaseUnits - fromLots:0.####} on product {line.ProductId}; correct with a stock adjustment.");
                }

                foreach (var (batchId, quantity, _) in issued.Where(i => i.StockBatchId != Guid.Empty))
                    line.BatchAllocations.Add(new SaleLineBatchAllocation
                    {
                        SaleLineBatchAllocationId = Guid.NewGuid(),
                        SaleLineId = line.SaleLineId,
                        StockBatchId = batchId,
                        QuantityInBaseUnits = quantity
                    });
                line.UnitCost = issued.Select(i => i.UnitCost).FirstOrDefault(c => c.HasValue);
            }
        }

        private static StockMovement Clone(StockMovement m, decimal quantityChange) => new()
        {
            ProductId = m.ProductId,
            BranchId = m.BranchId,
            MovementType = m.MovementType,
            QuantityChange = quantityChange,
            UnitCost = m.UnitCost,
            ReferenceType = m.ReferenceType,
            ReferenceId = m.ReferenceId,
            BatchId = m.BatchId,
            Notes = m.Notes,
            CreatedBy = m.CreatedBy
        };

        private static void AppendNote(Sale sale, string note) =>
            sale.Notes = string.IsNullOrWhiteSpace(sale.Notes) ? note : $"{sale.Notes} | {note}";

        public async Task FailPendingSaleAsync(Guid saleId, string reason)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            // Never fail a sale a concurrent confirmation has just completed: its stock has left and the customer has paid.
            await _context.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "Sales" WHERE "SaleId" = {saleId} FOR UPDATE""");
            var sale = await _context.Sales.FirstOrDefaultAsync(s => s.SaleId == saleId)
                ?? throw new NotFoundException($"Sale '{saleId}' not found.");
            if (sale.Status != SaleStatus.Pending)
                return;

            sale.Status = SaleStatus.Failed;
            AppendNote(sale, reason);
            sale.LastUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // ---- Reading ----

        public async Task<(IEnumerable<Sale> Items, int TotalCount)> GetSalesAsync(int page, int pageSize, SaleQuery filter,
            DateTime? fromUtc, DateTime? toUtcExclusive)
        {
            var query = _context.Sales.AsNoTracking().AsQueryable();

            if (filter.CustomerId.HasValue)
                query = query.Where(s => s.CustomerId == filter.CustomerId.Value);
            if (filter.Status.HasValue)
                query = query.Where(s => s.Status == filter.Status.Value);
            if (filter.CreatedBy.HasValue)
                query = query.Where(s => s.CreatedBy == filter.CreatedBy.Value);
            if (filter.PaymentMethod.HasValue)
                query = query.Where(s => s.PaymentMethod == filter.PaymentMethod.Value);
            if (fromUtc.HasValue)
                query = query.Where(s => s.CreatedAt >= fromUtc.Value);
            if (toUtcExclusive.HasValue)
                query = query.Where(s => s.CreatedAt < toUtcExclusive.Value);
            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var pattern = $"%{filter.Search.Trim()}%";
                query = query.Where(s => EF.Functions.ILike(s.SaleNumber, pattern)
                                         || (s.Customer != null && EF.Functions.ILike(s.Customer.CustomerName, pattern))
                                         || (s.MpesaReference != null && EF.Functions.ILike(s.MpesaReference, pattern)));
            }

            var totalCount = await query.CountAsync();
            var items = await query
                .Include(s => s.Customer)
                .Include(s => s.Branch)
                .Include(s => s.Prescription)
                .Include(s => s.Lines).ThenInclude(l => l.Product)
                .Include(s => s.Lines).ThenInclude(l => l.UnitOfMeasure)
                .OrderByDescending(s => s.CreatedAt).ThenBy(s => s.SaleId)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .AsSplitQuery()
                .ToListAsync();

            return (items, totalCount);
        }

        public async Task<Sale?> GetSaleByIdAsync(Guid saleId) =>
            await _context.Sales.AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.Branch)
                .Include(s => s.Prescription)
                .Include(s => s.Lines).ThenInclude(l => l.Product)
                .Include(s => s.Lines).ThenInclude(l => l.UnitOfMeasure)
                .Include(s => s.Lines).ThenInclude(l => l.BatchAllocations).ThenInclude(a => a.StockBatch)
                .AsSplitQuery()
                .FirstOrDefaultAsync(s => s.SaleId == saleId);

        public async Task<Dictionary<Guid, string>> GetUserNamesAsync(IEnumerable<Guid> userIds)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0)
                return new Dictionary<Guid, string>();
            return await _context.Users.AsNoTracking()
                .Where(u => ids.Contains(u.UserId))
                .Select(u => new { u.UserId, u.Username })
                .ToDictionaryAsync(u => u.UserId, u => u.Username);
        }

        // ---- Void ----

        public async Task VoidSaleAsync(Guid saleId, CustomerLedgerEntry? reversalLedgerEntry, Guid? updatedBy)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            // Serialise against a second void or a return, then re-check: the service validated against a snapshot, and a sale voided
            // twice would put its stock back twice.
            await _context.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "Sales" WHERE "SaleId" = {saleId} FOR UPDATE""");
            var sale = await _context.Sales.Include(s => s.Lines).ThenInclude(l => l.BatchAllocations).ThenInclude(a => a.StockBatch)
                           .FirstOrDefaultAsync(s => s.SaleId == saleId)
                       ?? throw new NotFoundException($"Sale '{saleId}' not found.");
            if (sale.Status != SaleStatus.Completed)
                throw new InvalidAssignmentException($"Only a Completed sale can be voided — this one is {sale.Status}.");
            if (sale.ReturnedAmount > 0)
                throw new InvalidAssignmentException("Goods have already been returned against this sale. Return the rest instead of voiding it.");

            sale.Status = SaleStatus.Voided;
            sale.LastUpdatedBy = updatedBy;
            sale.LastUpdatedAt = DateTime.UtcNow;

            foreach (var line in sale.Lines.OrderBy(l => l.ProductId))
                await RestockAsync(sale.BranchId, line, line.QuantityInBaseUnits, StockMovementType.SaleVoid, "Sale", saleId, "Sale voided", updatedBy,
                    markReturned: false);

            if (reversalLedgerEntry is not null)
                await _customerLedger.ApplyAsync(reversalLedgerEntry);

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }

        /// <summary>
        /// Puts <paramref name="quantityInBase"/> of a sale line back on the shelf at the cost it left at — into the lots it came from
        /// (latest-expiring first, never more than each lot gave), anything left over against the product total.
        /// </summary>
        private async Task RestockAsync(Guid branchId, SaleLine line, decimal quantityInBase, StockMovementType type, string referenceType, Guid referenceId,
            string notes, Guid? userId, bool markReturned)
        {
            decimal? unitCost = line.UnitCost > 0 ? line.UnitCost : null; // unknown cost leaves the average alone
            var remaining = quantityInBase;

            foreach (var allocation in line.BatchAllocations.OrderBy(a => a.StockBatch.ExpiryDate == null ? 0 : 1).ThenByDescending(a => a.StockBatch.ExpiryDate))
            {
                if (remaining <= 0) break;
                var capacity = allocation.QuantityInBaseUnits - allocation.QuantityReturnedInBaseUnits;
                if (capacity <= 0) continue;

                var part = Math.Min(capacity, remaining);
                remaining -= part;
                await _stockLedger.ApplyToBatchAsync(new StockMovement
                {
                    ProductId = line.ProductId, BranchId = branchId, MovementType = type, QuantityChange = part, UnitCost = unitCost,
                    ReferenceType = referenceType, ReferenceId = referenceId, BatchId = referenceId, Notes = notes, CreatedBy = userId
                }, allocation.StockBatchId);
                if (markReturned)
                    allocation.QuantityReturnedInBaseUnits += part;
            }

            if (remaining > 0)
                await _stockLedger.ApplyAsync(new StockMovement
                {
                    ProductId = line.ProductId, BranchId = branchId, MovementType = type, QuantityChange = remaining, UnitCost = unitCost,
                    ReferenceType = referenceType, ReferenceId = referenceId, BatchId = referenceId, Notes = notes, CreatedBy = userId
                }, allowNegative: true);
        }

        // ---- Returns ----

        public async Task<Dictionary<Guid, (decimal Amount, decimal TaxAmount)>> GetReturnedAmountsByLineAsync(Guid saleId) =>
            (await _context.SaleReturnLines.AsNoTracking()
                .Where(l => l.SaleReturn.SaleId == saleId)
                .GroupBy(l => l.SaleLineId)
                .Select(g => new { g.Key, Amount = g.Sum(l => l.Amount), Tax = g.Sum(l => l.TaxAmount) })
                .ToListAsync())
            .ToDictionary(r => r.Key, r => (r.Amount, r.Tax));

        public async Task RecordReturnAsync(SaleReturn saleReturn, CustomerLedgerEntry? creditNote)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            // Serialise returns against one sale, then re-check: the service validated against a snapshot.
            await _context.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "Sales" WHERE "SaleId" = {saleReturn.SaleId} FOR UPDATE""");
            var sale = await _context.Sales.Include(s => s.Lines).ThenInclude(l => l.BatchAllocations).ThenInclude(a => a.StockBatch)
                           .FirstOrDefaultAsync(s => s.SaleId == saleReturn.SaleId)
                       ?? throw new NotFoundException($"Sale '{saleReturn.SaleId}' not found.");
            if (sale.Status != SaleStatus.Completed)
                throw new InvalidAssignmentException($"Goods can only be returned against a Completed sale — this one is {sale.Status}.");

            // Returned goods go back into the stock of the branch that sold them.
            saleReturn.BranchId = sale.BranchId;
            foreach (var returned in saleReturn.Lines.OrderBy(l => l.ProductId))
            {
                var line = sale.Lines.First(l => l.SaleLineId == returned.SaleLineId);
                if (line.QuantityReturned + returned.Quantity > line.Quantity)
                    throw new InvalidAssignmentException("Part of this return has just been returned by someone else. Reload the sale and try again.");

                line.QuantityReturned += returned.Quantity;
                if (returned.Restock)
                    await RestockAsync(sale.BranchId, line, returned.QuantityInBaseUnits, StockMovementType.SaleReturn, "SaleReturn", saleReturn.SaleReturnId,
                        $"Returned: {saleReturn.Reason}", saleReturn.CreatedBy, markReturned: true);
            }

            sale.ReturnedAmount += saleReturn.TotalAmount;
            sale.LastUpdatedBy = saleReturn.CreatedBy;
            sale.LastUpdatedAt = DateTime.UtcNow;
            _context.SaleReturns.Add(saleReturn);

            if (creditNote is not null)
                await _customerLedger.ApplyAsync(creditNote);

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }

        public async Task<IEnumerable<SaleReturn>> GetReturnsForSaleAsync(Guid saleId) =>
            await _context.SaleReturns.AsNoTracking()
                .Include(r => r.Sale)
                .Include(r => r.Lines).ThenInclude(l => l.Product)
                .Where(r => r.SaleId == saleId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

        public async Task<(IEnumerable<SaleReturn> Items, int TotalCount)> GetReturnsAsync(int page, int pageSize, DateTime? fromUtc, DateTime? toUtcExclusive)
        {
            var query = _context.SaleReturns.AsNoTracking().AsQueryable();
            if (fromUtc.HasValue)
                query = query.Where(r => r.CreatedAt >= fromUtc.Value);
            if (toUtcExclusive.HasValue)
                query = query.Where(r => r.CreatedAt < toUtcExclusive.Value);

            var total = await query.CountAsync();
            var items = await query
                .Include(r => r.Sale)
                .Include(r => r.Lines).ThenInclude(l => l.Product)
                .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.SaleReturnId)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .AsSplitQuery()
                .ToListAsync();
            return (items, total);
        }

        // ---- Prescription register ----

        public async Task<IEnumerable<Sale>> GetPrescriptionSalesAsync(DateTime fromUtc, DateTime toUtcExclusive, string? search)
        {
            var query = _context.Sales.AsNoTracking()
                .Where(s => s.Prescription != null && s.Status != SaleStatus.Failed && s.CreatedAt >= fromUtc && s.CreatedAt < toUtcExclusive);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var pattern = $"%{search.Trim()}%";
                query = query.Where(s => EF.Functions.ILike(s.Prescription!.PatientName, pattern)
                                         || EF.Functions.ILike(s.Prescription!.PrescriberName, pattern)
                                         || (s.Prescription!.PrescriptionNumber != null && EF.Functions.ILike(s.Prescription!.PrescriptionNumber, pattern))
                                         || EF.Functions.ILike(s.SaleNumber, pattern));
            }

            return await query
                .Include(s => s.Prescription)
                .Include(s => s.Lines).ThenInclude(l => l.Product)
                .Include(s => s.Lines).ThenInclude(l => l.UnitOfMeasure)
                .Include(s => s.Lines).ThenInclude(l => l.BatchAllocations).ThenInclude(a => a.StockBatch)
                .OrderByDescending(s => s.CreatedAt)
                .AsSplitQuery()
                .ToListAsync();
        }
    }
}
