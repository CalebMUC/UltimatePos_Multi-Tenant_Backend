using Microsoft.EntityFrameworkCore;
using UltimatePos.Application.Inventory;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Stock transfers between branches. Dispatch takes the stock out of the source at its average cost (TransferOut movements);
    /// receipt puts what arrived into the destination at that same cost (TransferIn), so value moves with the goods.
    /// </summary>
    public class StockTransferRepository : IStockTransferRepository
    {
        private const string Reference = "StockTransfer";

        private readonly UltimatePosDbContext _context;
        private readonly StockLedger _stockLedger;

        public StockTransferRepository(UltimatePosDbContext context, StockLedger stockLedger)
        {
            _context = context;
            _stockLedger = stockLedger;
        }

        public async Task<StockTransfer> CreateAsync(StockTransfer transfer)
        {
            _context.StockTransfers.Add(transfer);
            await _context.SaveChangesAsync();
            return transfer;
        }

        public async Task<int> NextNumberAsync() => await _context.NextSequenceNumberAsync("TRF");

        private IQueryable<StockTransfer> WithDetails() =>
            _context.StockTransfers
                .Include(t => t.FromBranch)
                .Include(t => t.ToBranch)
                .Include(t => t.Lines).ThenInclude(l => l.Product)
                .Include(t => t.Lines).ThenInclude(l => l.UnitOfMeasure)
                .Include(t => t.Lines).ThenInclude(l => l.Batches)
                .AsSplitQuery();

        public async Task<(IReadOnlyList<StockTransfer> Items, int TotalCount)> GetTransfersAsync(int page, int pageSize, StockTransferStatus? status,
            Guid? branchId, IReadOnlyCollection<Guid>? visibleBranchIds)
        {
            var query = _context.StockTransfers.AsNoTracking().AsQueryable();
            if (status.HasValue)
                query = query.Where(t => t.Status == status.Value);
            if (branchId is { } b)
                query = query.Where(t => t.FromBranchId == b || t.ToBranchId == b);
            if (visibleBranchIds is not null)
            {
                var visible = visibleBranchIds.ToList();
                query = query.Where(t => visible.Contains(t.FromBranchId) || visible.Contains(t.ToBranchId));
            }

            var total = await query.CountAsync();
            var ids = await query.OrderByDescending(t => t.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
                .Select(t => t.StockTransferId).ToListAsync();
            var items = await WithDetails().AsNoTracking().Where(t => ids.Contains(t.StockTransferId)).ToListAsync();
            return (items.OrderByDescending(t => t.CreatedAt).ToList(), total);
        }

        public async Task<StockTransfer?> GetTransferAsync(Guid stockTransferId) =>
            await WithDetails().AsNoTracking().FirstOrDefaultAsync(t => t.StockTransferId == stockTransferId);

        /// <summary>Locks the transfer row, then re-reads it: the service checked the status against a snapshot.</summary>
        private async Task<StockTransfer> LockAsync(Guid stockTransferId, StockTransferStatus expected)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "StockTransfers" WHERE "StockTransferId" = {stockTransferId} FOR UPDATE""");
            var transfer = await WithDetails().FirstOrDefaultAsync(t => t.StockTransferId == stockTransferId)
                ?? throw new NotFoundException($"Stock transfer '{stockTransferId}' not found.");
            if (transfer.Status != expected)
                throw new InvalidAssignmentException($"{transfer.TransferNumber} has just been changed by someone else (it is now {transfer.Status}). Reload it.");
            return transfer;
        }

        public async Task DispatchAsync(Guid stockTransferId, Guid? userId, DateOnly today)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            var transfer = await LockAsync(stockTransferId, StockTransferStatus.Draft);

            // Product order, like every other multi-product stock operation, so concurrent operations lock rows in one sequence.
            foreach (var line in transfer.Lines.OrderBy(l => l.ProductId))
            {
                var movement = new StockMovement
                {
                    ProductId = line.ProductId,
                    BranchId = transfer.FromBranchId,
                    MovementType = StockMovementType.TransferOut,
                    QuantityChange = -line.QuantityInBaseUnits,
                    ReferenceType = Reference,
                    ReferenceId = transfer.StockTransferId,
                    BatchId = transfer.StockTransferId,
                    Notes = $"{transfer.TransferNumber} to {transfer.ToBranch.KraBranchId} {transfer.ToBranch.Name}",
                    CreatedBy = userId
                };

                if (!line.Product.IsBatchTracked)
                {
                    // A transfer can't send stock the branch doesn't have: no negative balances from transfers.
                    await _stockLedger.ApplyAsync(movement);
                    line.UnitCost = movement.UnitCost;
                    continue;
                }

                // Batch-tracked: unexpired lots, first-expiry-first-out. The lots travel with the stock (same number and expiry).
                var issued = await _stockLedger.IssueFefoAsync(movement, today);
                var lotIds = issued.Select(i => i.StockBatchId).ToList();
                var lots = await _context.StockBatches.IgnoreQueryFilters().AsNoTracking()
                    .Where(b => lotIds.Contains(b.StockBatchId))
                    .ToDictionaryAsync(b => b.StockBatchId);
                foreach (var (lotId, quantity, _) in issued)
                {
                    var lot = lots[lotId];
                    // Added explicitly: a new child with its key already set, added through a tracked parent's collection, would be
                    // taken for an existing row and UPDATEd.
                    _context.StockTransferLineBatches.Add(new StockTransferLineBatch
                    {
                        StockTransferLineBatchId = Guid.NewGuid(),
                        StockTransferLineId = line.StockTransferLineId,
                        SourceStockBatchId = lotId,
                        BatchNumber = lot.BatchNumber,
                        ExpiryDate = lot.ExpiryDate,
                        ManufactureDate = lot.ManufactureDate,
                        QuantityInBaseUnits = quantity
                    });
                }
                line.UnitCost = issued.Select(i => i.UnitCost).FirstOrDefault(c => c.HasValue);
            }

            transfer.Status = StockTransferStatus.Dispatched;
            transfer.DispatchedAt = DateTime.UtcNow;
            transfer.DispatchedBy = userId;
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }

        public async Task ReceiveAsync(Guid stockTransferId, IReadOnlyDictionary<Guid, decimal> receivedInBaseUnitsByLine, string? notes, Guid? userId)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            var transfer = await LockAsync(stockTransferId, StockTransferStatus.Dispatched);

            foreach (var line in transfer.Lines.OrderBy(l => l.ProductId))
            {
                var received = Math.Min(receivedInBaseUnitsByLine.GetValueOrDefault(line.StockTransferLineId, line.QuantityInBaseUnits),
                    line.QuantityInBaseUnits);
                line.QuantityReceivedInBaseUnits = received;
                if (received <= 0)
                    continue;

                // The stock arrives at what it was worth when it left, blended into the destination's own average.
                decimal? cost = line.UnitCost > 0 ? line.UnitCost : null;
                StockMovement Movement(decimal quantity) => new()
                {
                    ProductId = line.ProductId,
                    BranchId = transfer.ToBranchId,
                    MovementType = StockMovementType.TransferIn,
                    QuantityChange = quantity,
                    UnitCost = cost,
                    ReferenceType = Reference,
                    ReferenceId = transfer.StockTransferId,
                    BatchId = transfer.StockTransferId,
                    Notes = $"{transfer.TransferNumber} from {transfer.FromBranch.KraBranchId} {transfer.FromBranch.Name}",
                    CreatedBy = userId
                };

                if (line.Batches.Count == 0)
                {
                    await _stockLedger.ApplyAsync(Movement(received));
                    continue;
                }

                // A short delivery is counted against the soonest-expiring lots last: what arrived fills the lots in expiry order.
                var remaining = received;
                foreach (var lot in line.Batches.OrderBy(b => b.ExpiryDate ?? DateOnly.MaxValue).ThenBy(b => b.BatchNumber))
                {
                    var part = Math.Min(lot.QuantityInBaseUnits, remaining);
                    lot.QuantityReceivedInBaseUnits = part;
                    remaining -= part;
                    if (part > 0)
                        await _stockLedger.ReceiveIntoBatchAsync(Movement(part), transfer.BusinessId, lot.BatchNumber, lot.ExpiryDate,
                            lot.ManufactureDate, null, null);
                }
            }

            transfer.Status = StockTransferStatus.Received;
            transfer.ReceivedAt = DateTime.UtcNow;
            transfer.ReceivedBy = userId;
            transfer.ReceiveNotes = notes;
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }

        public async Task CancelAsync(Guid stockTransferId, Guid? userId)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            var transfer = await LockAsync(stockTransferId, StockTransferStatus.Draft);
            transfer.Status = StockTransferStatus.Cancelled;
            transfer.CancelledAt = DateTime.UtcNow;
            transfer.CancelledBy = userId;
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }
    }
}
