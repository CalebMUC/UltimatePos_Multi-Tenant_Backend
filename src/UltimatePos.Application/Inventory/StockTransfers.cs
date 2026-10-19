using UltimatePos.Application.Catalog;
using UltimatePos.Application.Branches;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Notifications;
using UltimatePos.Application.Reports;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Application.Inventory
{
    public record CreateStockTransferLineRequestDto(Guid ProductId, Guid UnitOfMeasureId, decimal Quantity);
    /// <param name="FromBranchId">Defaults to the branch signed in to.</param>
    public record CreateStockTransferRequestDto(Guid? FromBranchId, Guid ToBranchId, string? Notes, IEnumerable<CreateStockTransferLineRequestDto> Lines);
    /// <param name="QuantityReceived">In the unit the line was requested in.</param>
    public record ReceiveStockTransferLineRequestDto(Guid StockTransferLineId, decimal QuantityReceived);
    /// <param name="Lines">Leave out when everything arrived. Lines left out of a list arrived in full.</param>
    public record ReceiveStockTransferRequestDto(IEnumerable<ReceiveStockTransferLineRequestDto>? Lines, string? Notes);

    public record StockTransferLineBatchDto(string BatchNumber, DateOnly? ExpiryDate, decimal QuantityInBaseUnits, decimal? QuantityReceivedInBaseUnits);
    public record StockTransferLineDto(Guid StockTransferLineId, Guid ProductId, string ProductName, string Sku, Guid UnitOfMeasureId, string? UnitSymbol,
        decimal Quantity, decimal QuantityInBaseUnits, decimal? QuantityReceivedInBaseUnits, decimal ShortageInBaseUnits, decimal? UnitCost,
        IEnumerable<StockTransferLineBatchDto> Batches);
    public record StockTransferDto(Guid StockTransferId, string TransferNumber, Guid FromBranchId, string FromBranchName, string FromKraBranchId,
        Guid ToBranchId, string ToBranchName, string ToKraBranchId, StockTransferStatus Status, string? Notes, DateTime CreatedAt, Guid? CreatedBy,
        DateTime? DispatchedAt, DateTime? ReceivedAt, string? ReceiveNotes, decimal? DispatchedValue, decimal? ShortageValue,
        IEnumerable<StockTransferLineDto> Lines);

    public interface IStockTransferRepository
    {
        Task<StockTransfer> CreateAsync(StockTransfer transfer);
        /// <param name="visibleBranchIds">Null = every branch. Otherwise transfers from or to one of these.</param>
        Task<(IReadOnlyList<StockTransfer> Items, int TotalCount)> GetTransfersAsync(int page, int pageSize, StockTransferStatus? status,
            Guid? branchId, IReadOnlyCollection<Guid>? visibleBranchIds);
        Task<StockTransfer?> GetTransferAsync(Guid stockTransferId);
        /// <summary>Takes the stock out of the source branch (FEFO from unexpired lots for batch-tracked products) at its average cost.</summary>
        Task DispatchAsync(Guid stockTransferId, Guid? userId, DateOnly today);
        /// <summary>Puts what arrived into the destination branch at the dispatch cost — batch-tracked lots keep their number and expiry.</summary>
        Task ReceiveAsync(Guid stockTransferId, IReadOnlyDictionary<Guid, decimal> receivedInBaseUnitsByLine, string? notes, Guid? userId);
        Task CancelAsync(Guid stockTransferId, Guid? userId);
        Task<int> NextNumberAsync();
    }

    /// <summary>Moving stock between the branches of a business: Draft → Dispatched (in transit) → Received.</summary>
    public class StockTransferService
    {
        private readonly IStockTransferRepository _repository;
        private readonly IBranchRepository _branchRepository;
        private readonly ICatalogRepository _catalog;
        private readonly IBranchContext _branches;
        private readonly ITenantContext _tenant;
        private readonly ICurrentUser _currentUser;
        private readonly ISystemEventRecorder _events;

        public StockTransferService(IStockTransferRepository repository, IBranchRepository branchRepository, ICatalogRepository catalog,
            IBranchContext branches, ITenantContext tenant, ICurrentUser currentUser, ISystemEventRecorder events)
        {
            _repository = repository;
            _branchRepository = branchRepository;
            _catalog = catalog;
            _branches = branches;
            _tenant = tenant;
            _currentUser = currentUser;
            _events = events;
        }

        private static DateOnly Today => ReportPeriod.TodayAt(ReportPeriod.DefaultUtcOffsetMinutes);

        public async Task<StockTransferDto> CreateAsync(CreateStockTransferRequestDto request)
        {
            _tenant.RequireBusinessId();
            var fromId = request.FromBranchId ?? _branches.RequireActiveBranchId();
            if (!_branches.CanSee(fromId))
                throw new ForbiddenException("You can only send stock from a branch you work in.");
            var from = await RequireOpenBranchAsync(fromId, "fromBranchId");
            var to = await RequireOpenBranchAsync(request.ToBranchId, "toBranchId");
            if (from.BranchId == to.BranchId)
                throw new ValidationException("toBranchId", "Choose a different branch to send the stock to.");

            var lines = (request.Lines ?? Enumerable.Empty<CreateStockTransferLineRequestDto>()).ToList();
            if (lines.Count == 0)
                throw new ValidationException("lines", "A transfer needs at least one line.");
            if (lines.Any(l => l.Quantity <= 0))
                throw new ValidationException("lines", "Quantity must be greater than zero on every line.");
            if (lines.GroupBy(l => l.ProductId).Any(g => g.Count() > 1))
                throw new ValidationException("lines", "Each product can appear only once — combine the lines.");

            var products = (await _catalog.GetProductsByIdsAsync(lines.Select(l => l.ProductId))).ToDictionary(p => p.ProductId);
            var transferLines = new List<StockTransferLine>();
            foreach (var line in lines)
            {
                if (!products.TryGetValue(line.ProductId, out var product))
                    throw new NotFoundException($"Product '{line.ProductId}' not found.");
                if (!product.IsActive)
                    throw new ValidationException("lines", $"'{product.Name}' is inactive.");
                var factor = await _catalog.GetConversionFactorAsync(line.ProductId, line.UnitOfMeasureId)
                    ?? throw new ValidationException("lines", $"'{product.Name}' has no conversion for the unit given.");
                var baseQuantity = Math.Round(line.Quantity * factor, 4, MidpointRounding.AwayFromZero);
                if (baseQuantity <= 0)
                    throw new ValidationException("lines", $"The quantity of '{product.Name}' rounds to zero in its base unit.");
                transferLines.Add(new StockTransferLine
                {
                    StockTransferLineId = Guid.NewGuid(), ProductId = line.ProductId, UnitOfMeasureId = line.UnitOfMeasureId,
                    Quantity = line.Quantity, QuantityInBaseUnits = baseQuantity
                });
            }

            var transfer = new StockTransfer
            {
                StockTransferId = Guid.NewGuid(),
                TransferNumber = $"TRF-{await _repository.NextNumberAsync():D6}",
                FromBranchId = from.BranchId,
                ToBranchId = to.BranchId,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                CreatedBy = _currentUser.UserId,
                Lines = transferLines
            };
            _events.Record(new SystemEventDraft(SystemEventTypes.StockTransferCreated,
                $"{transfer.TransferNumber}: {transferLines.Count} line(s) to go from {from.KraBranchId} {from.Name} to {to.KraBranchId} {to.Name}.",
                EntityType: "StockTransfer", EntityId: transfer.StockTransferId, BranchId: from.BranchId));
            await _repository.CreateAsync(transfer);
            return await GetAsync(transfer.StockTransferId);
        }

        public async Task<PagedResult<StockTransferDto>> GetTransfersAsync(int page, int pageSize, StockTransferStatus? status, Guid? branchId)
        {
            _tenant.RequireBusinessId();
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            if (branchId is { } b && !_branches.CanSee(b))
                throw new ForbiddenException("You don't have access to that branch.");
            var (items, total) = await _repository.GetTransfersAsync(page, pageSize, status, branchId,
                _branches.Unrestricted ? null : _branches.VisibleBranchIds);
            return new PagedResult<StockTransferDto>(items.Select(ToDto), page, pageSize, total);
        }

        public async Task<StockTransferDto> GetAsync(Guid stockTransferId) => ToDto(await RequireVisibleAsync(stockTransferId));

        public async Task<StockTransferDto> DispatchAsync(Guid stockTransferId)
        {
            var transfer = await RequireVisibleAsync(stockTransferId);
            if (transfer.Status != StockTransferStatus.Draft)
                throw new InvalidAssignmentException($"{transfer.TransferNumber} is {transfer.Status}: only a draft can be dispatched.");
            if (!_branches.CanSee(transfer.FromBranchId))
                throw new ForbiddenException($"Only staff of {transfer.FromBranch.Name} can dispatch stock from it.");
            await RequireOpenBranchAsync(transfer.ToBranchId, "toBranchId");

            _events.Record(new SystemEventDraft(SystemEventTypes.StockTransferDispatched,
                $"{transfer.TransferNumber} left {transfer.FromBranch.KraBranchId} {transfer.FromBranch.Name} for {transfer.ToBranch.KraBranchId} {transfer.ToBranch.Name}: " +
                $"{transfer.Lines.Count} line(s). Receive it when it arrives.",
                EntityType: "StockTransfer", EntityId: stockTransferId, BranchId: transfer.ToBranchId));
            await _repository.DispatchAsync(stockTransferId, _currentUser.UserId, Today);
            return await GetAsync(stockTransferId);
        }

        public async Task<StockTransferDto> ReceiveAsync(Guid stockTransferId, ReceiveStockTransferRequestDto request)
        {
            var transfer = await RequireVisibleAsync(stockTransferId);
            if (transfer.Status != StockTransferStatus.Dispatched)
                throw new InvalidAssignmentException($"{transfer.TransferNumber} is {transfer.Status}: only a dispatched transfer can be received.");
            if (!_branches.CanSee(transfer.ToBranchId))
                throw new ForbiddenException($"Only staff of {transfer.ToBranch.Name} can receive stock into it.");

            var received = transfer.Lines.ToDictionary(l => l.StockTransferLineId, l => l.QuantityInBaseUnits);
            foreach (var given in request.Lines ?? Enumerable.Empty<ReceiveStockTransferLineRequestDto>())
            {
                var line = transfer.Lines.FirstOrDefault(l => l.StockTransferLineId == given.StockTransferLineId)
                    ?? throw new ValidationException("lines", $"Line '{given.StockTransferLineId}' is not on {transfer.TransferNumber}.");
                if (given.QuantityReceived < 0 || given.QuantityReceived > line.Quantity)
                    throw new ValidationException("lines", $"For '{line.Product.Name}', between 0 and {line.Quantity:0.####} can arrive.");
                received[line.StockTransferLineId] = Math.Round(line.QuantityInBaseUnits * given.QuantityReceived / line.Quantity, 4, MidpointRounding.AwayFromZero);
            }

            var shortLines = transfer.Lines.Where(l => received[l.StockTransferLineId] < l.QuantityInBaseUnits).ToList();
            var shortageValue = shortLines.Sum(l => (l.QuantityInBaseUnits - received[l.StockTransferLineId]) * (l.UnitCost ?? 0));
            _events.Record(new SystemEventDraft(SystemEventTypes.StockTransferReceived,
                $"{transfer.TransferNumber} was received at {transfer.ToBranch.KraBranchId} {transfer.ToBranch.Name}" +
                (shortLines.Count == 0 ? " in full." : $" with {shortLines.Count} line(s) short (worth {shortageValue:0.00}): " +
                    string.Join(", ", shortLines.Select(l => $"{l.Product.Name} {l.QuantityInBaseUnits - received[l.StockTransferLineId]:0.####} short")) + "."),
                shortLines.Count == 0 ? EventSeverity.Info : EventSeverity.Warning, "StockTransfer", stockTransferId,
                new { shortLines = shortLines.Count, shortageValue }, BranchId: transfer.FromBranchId));
            await _repository.ReceiveAsync(stockTransferId, received, string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(), _currentUser.UserId);
            return await GetAsync(stockTransferId);
        }

        public async Task<StockTransferDto> CancelAsync(Guid stockTransferId)
        {
            var transfer = await RequireVisibleAsync(stockTransferId);
            if (transfer.Status != StockTransferStatus.Draft)
                throw new InvalidAssignmentException($"{transfer.TransferNumber} is {transfer.Status}: only a draft can be cancelled — dispatched stock must be received.");
            _events.Record(new SystemEventDraft(SystemEventTypes.StockTransferCancelled, $"{transfer.TransferNumber} was cancelled.",
                EntityType: "StockTransfer", EntityId: stockTransferId, BranchId: transfer.FromBranchId));
            await _repository.CancelAsync(stockTransferId, _currentUser.UserId);
            return await GetAsync(stockTransferId);
        }

        private async Task<StockTransfer> RequireVisibleAsync(Guid stockTransferId)
        {
            _tenant.RequireBusinessId();
            var transfer = await _repository.GetTransferAsync(stockTransferId);
            if (transfer is null || (!_branches.CanSee(transfer.FromBranchId) && !_branches.CanSee(transfer.ToBranchId)))
                throw new NotFoundException($"Stock transfer '{stockTransferId}' not found.");
            return transfer;
        }

        private async Task<Branch> RequireOpenBranchAsync(Guid branchId, string field)
        {
            var branch = await _branchRepository.GetBranchAsync(branchId)
                ?? throw new ValidationException(field, "Branch not found.");
            if (!branch.IsActive)
                throw new ValidationException(field, $"Branch {branch.KraBranchId} {branch.Name} is closed.");
            return branch;
        }

        private static StockTransferDto ToDto(StockTransfer t)
        {
            var lines = t.Lines.OrderBy(l => l.Product?.Name).Select(l => new StockTransferLineDto(l.StockTransferLineId, l.ProductId,
                l.Product?.Name ?? string.Empty, l.Product?.Sku ?? string.Empty, l.UnitOfMeasureId, l.UnitOfMeasure?.Symbol, l.Quantity,
                l.QuantityInBaseUnits, l.QuantityReceivedInBaseUnits,
                l.QuantityReceivedInBaseUnits is { } r ? l.QuantityInBaseUnits - r : 0m, l.UnitCost,
                l.Batches.OrderBy(b => b.ExpiryDate).Select(b => new StockTransferLineBatchDto(b.BatchNumber, b.ExpiryDate, b.QuantityInBaseUnits,
                    b.QuantityReceivedInBaseUnits)).ToList())).ToList();
            decimal? Value(Func<StockTransferLineDto, decimal> quantity) =>
                t.Status is StockTransferStatus.Dispatched or StockTransferStatus.Received
                    ? Math.Round(lines.Sum(l => quantity(l) * (l.UnitCost ?? 0)), 2, MidpointRounding.AwayFromZero) : null;

            return new StockTransferDto(t.StockTransferId, t.TransferNumber, t.FromBranchId, t.FromBranch?.Name ?? string.Empty,
                t.FromBranch?.KraBranchId ?? string.Empty, t.ToBranchId, t.ToBranch?.Name ?? string.Empty, t.ToBranch?.KraBranchId ?? string.Empty,
                t.Status, t.Notes, t.CreatedAt, t.CreatedBy, t.DispatchedAt, t.ReceivedAt, t.ReceiveNotes,
                Value(l => l.QuantityInBaseUnits), t.Status == StockTransferStatus.Received ? Value(l => l.ShortageInBaseUnits) : null, lines);
        }
    }
}
