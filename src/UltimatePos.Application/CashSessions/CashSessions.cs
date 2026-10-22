using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Notifications;
using UltimatePos.Application.Reports;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.CashSessions
{
    /// <param name="OpeningFloat">The cash put in the drawer for giving change.</param>
    public record OpenCashSessionRequestDto(decimal OpeningFloat, string? Notes);
    /// <summary>PayIn: cash added (a float top-up, a rider handing back change). PayOut: cash taken out (banking, change given to a rider).</summary>
    public record RecordCashMovementRequestDto(CashMovementType MovementType, decimal Amount, string Reason);
    /// <param name="CountedCash">Everything counted in the drawer at the close, the float included.</param>
    public record CloseCashSessionRequestDto(decimal CountedCash, string? Notes);

    public record CashSessionMovementDto(Guid CashSessionMovementId, CashMovementType MovementType, decimal Amount, string Reason,
        DateTime CreatedAt, Guid? CreatedBy, string? CreatedByName);

    /// <summary>
    /// What should be in the drawer: OpeningFloat + CashSales + CustomerPayments + PayIns - CashRefunds - CashExpenses - SupplierPayments
    /// - PayOuts. Each counts cash only, at the session's branch, while it was open.
    /// </summary>
    /// <param name="CashSales">Completed cash sales, delivery sales included (the cash the riders handed in), net of the change given.</param>
    /// <param name="CashStillWithRiders">What orders still out for delivery from this branch should bring back in cash. Not in the drawer
    /// yet, so not in ExpectedCash: record those deliveries before counting.</param>
    public record CashSessionBreakdownDto(decimal OpeningFloat, decimal CashSales, int CashSaleCount, decimal CustomerPayments, decimal PayIns,
        decimal CashRefunds, decimal CashExpenses, decimal SupplierPayments, decimal PayOuts, decimal ExpectedCash,
        decimal CashStillWithRiders, int OrdersStillOut);

    /// <param name="Breakdown">Live while open; fixed at the close (ExpectedCash then equals the stored figure).</param>
    /// <param name="Variance">CountedCash - ExpectedCash once closed: negative = the drawer is short.</param>
    public record CashSessionDto(Guid CashSessionId, string SessionNumber, CashSessionStatus Status, Guid BranchId, string? KraBranchId,
        string? BranchName, decimal OpeningFloat, DateTime OpenedAt, Guid? OpenedBy, string? OpenedByName, string? OpeningNotes,
        decimal? CountedCash, decimal? Variance, DateTime? ClosedAt, Guid? ClosedBy, string? ClosedByName, string? ClosingNotes,
        CashSessionBreakdownDto Breakdown, IEnumerable<CashSessionMovementDto> Movements);

    /// <summary>from/to: the local days sessions were opened on, inclusive.</summary>
    public record CashSessionQuery(DateOnly? From = null, DateOnly? To = null, CashSessionStatus? Status = null,
        int UtcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes);

    /// <summary>Cash in and out of one branch's drawer in a window, by source.</summary>
    public record CashFlowTotals(decimal CashSales, int CashSaleCount, decimal CustomerPayments, decimal CashRefunds, decimal CashExpenses,
        decimal SupplierPayments, decimal CashStillWithRiders, int OrdersStillOut);

    public interface ICashSessionRepository
    {
        Task<int> NextNumberAsync();
        /// <summary>False when the branch already has an open session (the unique index decides, so two at once give one).</summary>
        Task<bool> OpenAsync(CashSession session);
        /// <summary>With branch and movements.</summary>
        Task<CashSession?> GetAsync(Guid cashSessionId);
        Task<CashSession?> GetOpenAsync(Guid branchId);
        Task<(IReadOnlyList<CashSession> Items, int TotalCount)> GetSessionsAsync(int page, int pageSize, CashSessionStatus? status,
            DateTime? fromUtc, DateTime? toUtcExclusive);
        /// <summary>Adds the movement only while the session is still open (checked under a lock). False otherwise.</summary>
        Task<bool> AddMovementAsync(CashSessionMovement movement);
        /// <summary>Open → Closed with the figures, in one conditional update that commits with the events recorded for it. False when it
        /// was no longer open.</summary>
        Task<bool> CloseAsync(Guid cashSessionId, decimal expectedCash, decimal countedCash, string? notes, Guid? userId);
        Task<CashFlowTotals> GetCashFlowsAsync(Guid branchId, DateTime fromUtc, DateTime toUtcExclusive);
        Task<Dictionary<Guid, string>> GetUserNamesAsync(IEnumerable<Guid> userIds);
    }

    /// <summary>
    /// The float and the end-of-day cash-up. A manager opens the day with the float allocated for change; the cash-up counts the drawer
    /// and compares it with what the day's cash sales, payments, refunds, expenses and pay-ins/outs say should be there. Nothing here
    /// moves money: it accounts for it.
    /// </summary>
    public class CashSessionService
    {
        private const decimal MaxAmount = 100_000_000m;
        private const int MaxTextLength = 500;

        private readonly ICashSessionRepository _repository;
        private readonly IBranchContext _branches;
        private readonly ITenantContext _tenant;
        private readonly ICurrentUser _currentUser;
        private readonly ISystemEventRecorder _events;

        public CashSessionService(ICashSessionRepository repository, IBranchContext branches, ITenantContext tenant, ICurrentUser currentUser,
            ISystemEventRecorder events)
        {
            _repository = repository;
            _branches = branches;
            _tenant = tenant;
            _currentUser = currentUser;
            _events = events;
        }

        public async Task<CashSessionDto> OpenAsync(OpenCashSessionRequestDto request)
        {
            _tenant.RequireBusinessId();
            ValidateAmount(request.OpeningFloat, "openingFloat", allowZero: true);
            var notes = OptionalText(request.Notes, "notes");
            var branchId = _branches.RequireActiveBranchId();
            if (await _repository.GetOpenAsync(branchId) is { } open)
                throw new InvalidAssignmentException($"{open.SessionNumber} is still open at this branch: close it before opening another.");

            var session = new CashSession
            {
                CashSessionId = Guid.NewGuid(),
                SessionNumber = $"CSH-{await _repository.NextNumberAsync():D6}",
                BranchId = branchId,
                OpeningFloat = RoundMoney(request.OpeningFloat),
                OpenedAt = DateTime.UtcNow,
                OpenedBy = _currentUser.UserId,
                OpeningNotes = notes
            };
            _events.Record(new SystemEventDraft(SystemEventTypes.CashSessionOpened,
                $"{session.SessionNumber} opened with a float of {session.OpeningFloat:0.00}.",
                EntityType: "CashSession", EntityId: session.CashSessionId, Data: new { session.OpeningFloat }, BranchId: branchId));
            if (!await _repository.OpenAsync(session))
                throw new InvalidAssignmentException("A cash session has just been opened at this branch by someone else. Reload it.");
            return await GetAsync(session.CashSessionId);
        }

        /// <summary>The open session at the branch signed in to, or null.</summary>
        public async Task<CashSessionDto?> GetCurrentAsync()
        {
            _tenant.RequireBusinessId();
            var open = await _repository.GetOpenAsync(_branches.RequireActiveBranchId());
            return open is null ? null : await GetAsync(open.CashSessionId);
        }

        public async Task<CashSessionDto> GetAsync(Guid cashSessionId)
        {
            var session = await RequireAsync(cashSessionId);
            var breakdown = await BreakdownAsync(session);
            var names = await _repository.GetUserNamesAsync(new[] { session.OpenedBy, session.ClosedBy }
                .Concat(session.Movements.Select(m => m.CreatedBy)).OfType<Guid>());
            return ToDto(session, breakdown, names);
        }

        public async Task<PagedResult<CashSessionDto>> GetSessionsAsync(int page, int pageSize, CashSessionQuery query)
        {
            _tenant.RequireBusinessId();
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            ReportPeriod.ValidateOffset(query.UtcOffsetMinutes);
            if (query.From.HasValue && query.To.HasValue && query.From > query.To)
                throw new ValidationException("from", "'from' cannot be after 'to'.");
            var offset = TimeSpan.FromMinutes(query.UtcOffsetMinutes);
            var (items, total) = await _repository.GetSessionsAsync(page, pageSize, query.Status,
                query.From is { } f ? ReportPeriod.StartOfLocalDayUtc(f, offset) : null,
                query.To is { } t ? ReportPeriod.StartOfLocalDayUtc(t.AddDays(1), offset) : null);
            var names = await _repository.GetUserNamesAsync(items.SelectMany(s => new[] { s.OpenedBy, s.ClosedBy }
                .Concat(s.Movements.Select(m => m.CreatedBy))).OfType<Guid>());
            var result = new List<CashSessionDto>();
            foreach (var session in items)
                result.Add(ToDto(session, await BreakdownAsync(session), names));
            return new PagedResult<CashSessionDto>(result, page, pageSize, total);
        }

        public async Task<CashSessionDto> RecordMovementAsync(Guid cashSessionId, RecordCashMovementRequestDto request)
        {
            var session = await RequireAsync(cashSessionId);
            if (!Enum.IsDefined(request.MovementType))
                throw new ValidationException("movementType", "A movement is a PayIn (cash added) or a PayOut (cash taken out).");
            ValidateAmount(request.Amount, "amount", allowZero: false);
            var reason = OptionalText(request.Reason, "reason")
                ?? throw new ValidationException("reason", "What was the cash for? A reason is required (e.g. banked at Equity, change for rider Peter).");
            if (session.Status != CashSessionStatus.Open)
                throw new InvalidAssignmentException($"{session.SessionNumber} is closed: cash can no longer be recorded against it.");

            var movement = new CashSessionMovement
            {
                CashSessionMovementId = Guid.NewGuid(),
                CashSessionId = cashSessionId,
                MovementType = request.MovementType,
                Amount = RoundMoney(request.Amount),
                Reason = reason,
                CreatedBy = _currentUser.UserId
            };
            _events.Record(new SystemEventDraft(SystemEventTypes.CashSessionMovementRecorded,
                $"{session.SessionNumber}: {(movement.MovementType == CashMovementType.PayIn ? "paid in" : "paid out")} {movement.Amount:0.00} — {reason}",
                EntityType: "CashSession", EntityId: cashSessionId, Data: new { movement.MovementType, movement.Amount, reason }, BranchId: session.BranchId));
            if (!await _repository.AddMovementAsync(movement))
                throw new InvalidAssignmentException($"{session.SessionNumber} has just been closed: cash can no longer be recorded against it.");
            return await GetAsync(cashSessionId);
        }

        /// <summary>
        /// The end-of-day count. What was expected is worked out now and fixed with the count, so later changes (a void tomorrow) never
        /// move a closed day. A difference raises an alert to the managers who allocate floats.
        /// </summary>
        public async Task<CashSessionDto> CloseAsync(Guid cashSessionId, CloseCashSessionRequestDto request)
        {
            var session = await RequireAsync(cashSessionId);
            ValidateAmount(request.CountedCash, "countedCash", allowZero: true);
            var notes = OptionalText(request.Notes, "notes");
            if (session.Status != CashSessionStatus.Open)
                throw new InvalidAssignmentException($"{session.SessionNumber} is already closed.");

            var breakdown = await BreakdownAsync(session);
            var counted = RoundMoney(request.CountedCash);
            var variance = counted - breakdown.ExpectedCash;
            _events.Record(new SystemEventDraft(SystemEventTypes.CashSessionClosed,
                $"{session.SessionNumber} closed: expected {breakdown.ExpectedCash:0.00}, counted {counted:0.00}.",
                EntityType: "CashSession", EntityId: cashSessionId, BranchId: session.BranchId,
                Data: new { breakdown.ExpectedCash, counted, variance, breakdown.CashStillWithRiders }));
            if (variance != 0)
                _events.Record(new SystemEventDraft(SystemEventTypes.CashSessionVariance,
                    $"{session.SessionNumber} counted {(variance < 0 ? "short" : "over")} by {Math.Abs(variance):0.00} " +
                    $"(expected {breakdown.ExpectedCash:0.00}, counted {counted:0.00}).",
                    EventSeverity.Warning, "CashSession", cashSessionId, new { breakdown.ExpectedCash, counted, variance }, BranchId: session.BranchId));
            if (!await _repository.CloseAsync(cashSessionId, breakdown.ExpectedCash, counted, notes, _currentUser.UserId))
                throw new InvalidAssignmentException($"{session.SessionNumber} has just been closed by someone else. Reload it.");
            return await GetAsync(cashSessionId);
        }

        // ---- Helpers ----

        private async Task<CashSessionBreakdownDto> BreakdownAsync(CashSession session)
        {
            var until = session.ClosedAt ?? DateTime.UtcNow.AddSeconds(1);
            var flows = await _repository.GetCashFlowsAsync(session.BranchId, session.OpenedAt, until);
            var payIns = session.Movements.Where(m => m.MovementType == CashMovementType.PayIn).Sum(m => m.Amount);
            var payOuts = session.Movements.Where(m => m.MovementType == CashMovementType.PayOut).Sum(m => m.Amount);
            var expected = session.OpeningFloat + flows.CashSales + flows.CustomerPayments + payIns
                           - flows.CashRefunds - flows.CashExpenses - flows.SupplierPayments - payOuts;
            // A closed day keeps the figure it was counted against.
            if (session.Status == CashSessionStatus.Closed && session.ExpectedCash is { } stored)
                expected = stored;
            return new CashSessionBreakdownDto(session.OpeningFloat, flows.CashSales, flows.CashSaleCount, flows.CustomerPayments, payIns,
                flows.CashRefunds, flows.CashExpenses, flows.SupplierPayments, payOuts, expected,
                session.Status == CashSessionStatus.Open ? flows.CashStillWithRiders : 0m,
                session.Status == CashSessionStatus.Open ? flows.OrdersStillOut : 0);
        }

        private async Task<CashSession> RequireAsync(Guid cashSessionId)
        {
            _tenant.RequireBusinessId();
            return await _repository.GetAsync(cashSessionId)
                ?? throw new NotFoundException($"Cash session '{cashSessionId}' not found.");
        }

        private static void ValidateAmount(decimal amount, string field, bool allowZero)
        {
            if (amount < 0 || (!allowZero && amount == 0))
                throw new ValidationException(field, allowZero ? "The amount cannot be negative." : "The amount must be greater than zero.");
            if (amount > MaxAmount)
                throw new ValidationException(field, "The amount is too large.");
            if (decimal.Round(amount, 2) != amount)
                throw new ValidationException(field, "Amounts have at most 2 decimal places.");
        }

        private static string? OptionalText(string? value, string field)
        {
            var text = value?.Trim();
            if (string.IsNullOrEmpty(text))
                return null;
            if (text.Length > MaxTextLength)
                throw new ValidationException(field, $"At most {MaxTextLength} characters.");
            return text;
        }

        private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private static CashSessionDto ToDto(CashSession s, CashSessionBreakdownDto breakdown, IReadOnlyDictionary<Guid, string> names)
        {
            string? Name(Guid? id) => id is { } u && names.TryGetValue(u, out var n) ? n : null;
            return new CashSessionDto(s.CashSessionId, s.SessionNumber, s.Status, s.BranchId,
                s.Branch is null ? null : Branch.FormatKraBranchId(s.Branch.BranchNumber), s.Branch?.Name,
                s.OpeningFloat, s.OpenedAt, s.OpenedBy, Name(s.OpenedBy), s.OpeningNotes,
                s.CountedCash, s.Variance, s.ClosedAt, s.ClosedBy, Name(s.ClosedBy), s.ClosingNotes, breakdown,
                s.Movements.OrderBy(m => m.CreatedAt).Select(m => new CashSessionMovementDto(m.CashSessionMovementId, m.MovementType, m.Amount,
                    m.Reason, m.CreatedAt, m.CreatedBy, Name(m.CreatedBy))).ToList());
        }
    }
}
