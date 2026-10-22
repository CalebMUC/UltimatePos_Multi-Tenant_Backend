using UltimatePos.Application.Business;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Helpers;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Notifications;
using UltimatePos.Application.Reports;
using UltimatePos.Application.Sales;
using UltimatePos.Application.Sales.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Deliveries
{
    /// <summary>One line of an order: two 20L refills is (water, 20L, 2); an empty container bought with it is a line of its own.</summary>
    public record DeliveryOrderLineRequestDto(Guid ProductId, Guid UnitOfMeasureId, decimal Quantity);
    /// <summary>A bundle ordered as one item ("2 x 20L Heavy-duty bottle + water"), priced as the bundle. Whole bundles only.</summary>
    public record DeliveryOrderBundleRequestDto(Guid ProductBundleId, decimal Quantity);
    /// <param name="ContactPhone">Who to call on arrival. Defaults to the customer's phone.</param>
    /// <param name="Bundles">Bundles on the order. An order needs at least one line or one bundle.</param>
    /// <param name="ExpectedPaymentMethod">How the customer will pay, chosen at the till (the mode of payment of a rider delivery): Cash
    /// (the rider collects it), MpesaReference (the rider brings back the M-Pesa code), MpesaTill or Credit. Defaults to Cash.</param>
    /// <param name="RiderName">Given when the rider leaves at once (checkout with delivery by rider): the order goes straight out for
    /// delivery, on a new trip or on <paramref name="DeliveryTripId"/>.</param>
    /// <param name="DeliveryTripId">A trip still out with the same rider to add this order to (they have not left yet).</param>
    public record CreateDeliveryOrderRequestDto(Guid CustomerId, string DeliveryAddress, string? ContactPhone, string? Notes,
        IEnumerable<DeliveryOrderLineRequestDto>? Lines, IEnumerable<DeliveryOrderBundleRequestDto>? Bundles = null,
        PaymentMethod? ExpectedPaymentMethod = null, string? RiderName = null, Guid? DeliveryTripId = null);
    /// <param name="DeliveryTripId">Add it to a trip still out (RiderName may then be left out: it is the trip's rider).</param>
    public record DispatchDeliveryOrderRequestDto(string? RiderName, Guid? DeliveryTripId = null);
    /// <summary>Several pending orders out with one rider: one trip.</summary>
    public record DispatchTripRequestDto(string? RiderName, IEnumerable<Guid> DeliveryOrderIds, Guid? DeliveryTripId = null);
    /// <summary>
    /// Recorded when the customer has received the goods (typically by the cashier once the rider is back with the money). This is what
    /// completes the sale. Payment is Cash (AmountTendered), MpesaTill (MpesaTransactionId of the till payment), MpesaReference (the
    /// M-Pesa code from the customer's SMS, which the rider must bring back) or Credit — a promise to pay by PaymentDueDate, which needs
    /// DeliveryOrders.AcceptCredit.
    /// </summary>
    /// <param name="Lines">What was actually handed over, when it differs from the order (say the customer had a container after all).
    /// Left out, with Bundles, = exactly as ordered. The sale is priced from these.</param>
    /// <param name="Bundles">The bundles actually handed over. Give Lines and/or Bundles to change the order; leave both out to keep it.</param>
    public record DeliverDeliveryOrderRequestDto(PaymentMethod PaymentMethod, decimal? AmountTendered = null, Guid? MpesaTransactionId = null,
        DateOnly? PaymentDueDate = null, bool OverrideCreditLimit = false, IEnumerable<DeliveryOrderLineRequestDto>? Lines = null, string? Notes = null,
        string? MpesaReference = null, IEnumerable<DeliveryOrderBundleRequestDto>? Bundles = null);
    public record CancelDeliveryOrderRequestDto(string Reason);
    /// <summary>The rider came back with the goods undelivered (nobody home, refused, wrong address).</summary>
    public record ReturnDeliveryOrderRequestDto(string Reason);

    /// <param name="ProductBundleId">Set on the lines of a bundle: one line per part, sharing it. BundleQuantity is how many bundles.</param>
    public record DeliveryOrderLineDto(Guid DeliveryOrderLineId, Guid ProductId, string ProductName, string Sku, Guid UnitOfMeasureId,
        string? UnitSymbol, decimal Quantity, decimal QuantityInBaseUnits, Guid? ProductBundleId = null, string? BundleName = null,
        decimal? BundleQuantity = null);
    /// <param name="EstimatedTotal">At the customer's prices when the order was taken. The sale (SaleTotal) is what was charged. While
    /// the order is open it is what the rider should come back with.</param>
    /// <param name="ExpectedPaymentMethod">How the customer said they will pay. Null on orders taken before it was asked.</param>
    public record DeliveryOrderDto(Guid DeliveryOrderId, string OrderNumber, DeliveryOrderStatus Status, Guid CustomerId, string CustomerName,
        string DeliveryAddress, string? ContactPhone, string? Notes, decimal EstimatedTotal,
        string? RiderName, DateTime? DispatchedAt,
        Guid? SaleId, string? SaleNumber, decimal? SaleTotal, PaymentMethod? PaymentMethod, DateOnly? PaymentDueDate, DateTime? DeliveredAt,
        DateTime? CancelledAt, string? CancelReason, DateTime CreatedAt, Guid? CreatedBy,
        Guid BranchId, string? KraBranchId, string? BranchName, IEnumerable<DeliveryOrderLineDto> Lines,
        PaymentMethod? ExpectedPaymentMethod = null, Guid? DeliveryTripId = null, string? TripNumber = null,
        DateTime? ReturnedAt = null, string? ReturnReason = null);

    /// <summary>Filters for the list. from/to are local days the order was TAKEN, inclusive.</summary>
    public record DeliveryOrderQuery(DeliveryOrderStatus? Status = null, Guid? CustomerId = null, string? RiderName = null,
        DateOnly? From = null, DateOnly? To = null, int UtcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes, Guid? DeliveryTripId = null);

    /// <summary>
    /// What each rider delivered in the period (by delivery day) and how it was paid — CashCollected is the cash they should hand in.
    /// PaidByMpesaTill is everything paid by M-Pesa: a matched till payment or a typed M-Pesa code. OutForDeliveryNow is as of now, whatever
    /// the period, and CashExpectedNow is what the orders they have out now should bring back in cash.
    /// </summary>
    public record RiderSummaryDto(string RiderName, int DeliveredCount, decimal DeliveredTotal, decimal CashCollected, decimal PaidByMpesaTill,
        decimal OnCredit, int OutForDeliveryNow, int ReturnedCount = 0, int TripCount = 0, decimal CashExpectedNow = 0);
    public record DeliverySummaryDto(DateOnly From, DateOnly To, int DeliveredCount, decimal DeliveredTotal, decimal CashCollected,
        decimal PaidByMpesaTill, decimal OnCredit, int CancelledCount, int PendingNow, int OutForDeliveryNow, IEnumerable<RiderSummaryDto> ByRider,
        int ReturnedCount = 0, int TripCount = 0);

    public record DeliveryTripOrderDto(Guid DeliveryOrderId, string OrderNumber, DeliveryOrderStatus Status, string CustomerName,
        string DeliveryAddress, decimal EstimatedTotal, string? SaleNumber, decimal? SaleTotal);
    /// <param name="IsBack">Every order on it is delivered, returned or cancelled.</param>
    /// <param name="IsWasted">Back, and nothing on it was delivered: the fuel bought nothing.</param>
    public record DeliveryTripDto(Guid DeliveryTripId, string TripNumber, string RiderName, DateTime DispatchedAt, Guid BranchId,
        int OrderCount, int DeliveredCount, int ReturnedCount, int OutCount, decimal DeliveredTotal, bool IsBack, bool IsWasted,
        IEnumerable<DeliveryTripOrderDto> Orders);
    /// <summary>from/to: the local days trips were dispatched on.</summary>
    public record DeliveryTripQuery(DateOnly From, DateOnly To, string? RiderName = null, int UtcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes);

    /// <summary>
    /// One rider's trips weighed against the fuel paid for them. FuelSpent is the posted expenses tagged with the rider, dated in the
    /// period (in ExpenseCategoryId when the report was asked for one). ExpectedFuel needs an expected cost per trip.
    /// </summary>
    /// <param name="WastedTrips">Trips that came back with nothing delivered.</param>
    /// <param name="SingleOrderTrips">Trips that carried one order: candidates for being combined with another.</param>
    /// <param name="FuelVariance">FuelSpent - ExpectedFuel: positive = more fuel than the trips account for.</param>
    /// <param name="AboveAverage">Fuel per trip is more than 20% above the business's average for the period.</param>
    public record RiderFuelDto(string RiderName, int TripCount, int OrdersCarried, int DeliveredCount, int ReturnedCount, int WastedTrips,
        int SingleOrderTrips, decimal FuelSpent, int FuelExpenseCount, decimal? FuelPerTrip, decimal? ExpectedFuel, decimal? FuelVariance,
        bool FuelAccountedFor, bool AboveAverage);
    public record DeliveryFuelReportDto(DateOnly From, DateOnly To, Guid? ExpenseCategoryId, decimal? ExpectedFuelPerTrip,
        int TripCount, int WastedTrips, decimal FuelSpent, decimal? AverageFuelPerTrip, decimal? ExpectedFuel, decimal? FuelVariance,
        IEnumerable<RiderFuelDto> ByRider);

    /// <summary>A posted expense tagged with a rider, reduced to what the fuel report needs.</summary>
    public record RiderExpenseRow(string RiderName, decimal Amount);

    public interface IDeliveryOrderRepository
    {
        Task<int> NextNumberAsync();
        Task<int> NextTripNumberAsync();
        /// <summary>Saves the order, and the trip when it starts a new one.</summary>
        Task CreateAsync(DeliveryOrder order, DeliveryTrip? newTrip);
        /// <summary>With its customer, branch, trip, lines and (once delivered) sale.</summary>
        Task<DeliveryOrder?> GetAsync(Guid deliveryOrderId);
        Task<IReadOnlyList<DeliveryOrder>> GetManyAsync(IEnumerable<Guid> deliveryOrderIds);
        Task<(IReadOnlyList<DeliveryOrder> Items, int TotalCount)> GetOrdersAsync(int page, int pageSize, DeliveryOrderQuery query,
            DateTime? fromUtc, DateTime? toUtcExclusive);
        /// <summary>With its orders.</summary>
        Task<DeliveryTrip?> GetTripAsync(Guid deliveryTripId);
        /// <summary>Every order Pending → OutForDelivery on the trip, saving the trip when it is new, in one transaction that commits with
        /// the events recorded for it. False (and nothing saved) when any of them was no longer pending.</summary>
        Task<bool> DispatchAsync(IReadOnlyCollection<Guid> deliveryOrderIds, DeliveryTrip trip, bool newTrip, Guid? userId);
        /// <summary>Pending or OutForDelivery → Cancelled, in one conditional update that commits with the events recorded for it. False
        /// (and nothing saved) when it was no longer open.</summary>
        Task<bool> CancelAsync(Guid deliveryOrderId, string reason, Guid? userId);
        /// <summary>OutForDelivery → Returned, the same way. False (and nothing saved) when it was no longer out for delivery.</summary>
        Task<bool> ReturnAsync(Guid deliveryOrderId, string reason, Guid? userId);
        /// <summary>Orders delivered, cancelled or returned in the window, and every order open now, with their sales.</summary>
        Task<IReadOnlyList<DeliveryOrder>> GetForSummaryAsync(DateTime fromUtc, DateTime toUtcExclusive);
        /// <summary>Trips dispatched in the window, with their orders (customer and sale), oldest first.</summary>
        Task<IReadOnlyList<DeliveryTrip>> GetTripsAsync(DateTime fromUtc, DateTime toUtcExclusive, string? riderName);
        /// <summary>Posted expenses tagged with a rider, dated from..to inclusive, in one category when given.</summary>
        Task<IReadOnlyList<RiderExpenseRow>> GetRiderExpensesAsync(DateOnly from, DateOnly toInclusive, Guid? expenseCategoryId);
    }

    /// <summary>
    /// Orders taken for delivery: Pending (stock reserved) → OutForDelivery (with a rider, on a trip) → Delivered (an ordinary sale, paid
    /// by cash, an M-Pesa code, a till payment or on credit), or Cancelled, or Returned when the rider brings it back. A checkout with
    /// delivery by rider is an order like any other: it is not a sale until the rider is back with the money. Nothing here moves stock or
    /// money itself: the sale does, once, when the order is delivered.
    /// </summary>
    public class DeliveryOrderService
    {
        public const string AcceptCreditPermission = "DeliveryOrders.AcceptCredit";
        public const string DispatchPermission = "DeliveryOrders.Dispatch";
        private const int MaxAddressLength = 500;
        private const int MaxRiderNameLength = 100;
        private const int MaxTextLength = 1000;
        private const int MaxOrdersPerTrip = 50;
        /// <summary>A rider whose fuel per trip is this much above the business's average is flagged.</summary>
        private const decimal AboveAverageFactor = 1.2m;

        private readonly IDeliveryOrderRepository _repository;
        private readonly SalesService _sales;
        private readonly IBusinessRepository _businessRepository;
        private readonly IBranchContext _branches;
        private readonly ITenantContext _tenant;
        private readonly ICurrentUser _currentUser;
        private readonly ISystemEventRecorder _events;

        public DeliveryOrderService(IDeliveryOrderRepository repository, SalesService sales, IBusinessRepository businessRepository,
            IBranchContext branches, ITenantContext tenant, ICurrentUser currentUser, ISystemEventRecorder events)
        {
            _repository = repository;
            _sales = sales;
            _businessRepository = businessRepository;
            _branches = branches;
            _tenant = tenant;
            _currentUser = currentUser;
            _events = events;
        }

        public async Task<DeliveryOrderDto> CreateAsync(CreateDeliveryOrderRequestDto request)
        {
            var businessId = _tenant.RequireBusinessId();
            var customer = await _businessRepository.GetCustomerByIdAsync(request.CustomerId)
                ?? throw new NotFoundException($"Customer '{request.CustomerId}' not found.");
            if (!customer.IsActive)
                throw new InvalidAssignmentException("This customer is deactivated.");

            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.DeliveryAddress))
                errors["deliveryAddress"] = new[] { "Where should it be delivered? An address or directions are required." };
            else if (request.DeliveryAddress.Trim().Length > MaxAddressLength)
                errors["deliveryAddress"] = new[] { $"The address is at most {MaxAddressLength} characters." };
            if (request.Notes is { Length: > MaxTextLength })
                errors["notes"] = new[] { $"Notes are at most {MaxTextLength} characters." };
            if (request.ExpectedPaymentMethod is { } expected && !IsDeliveryPayment(expected))
                errors["expectedPaymentMethod"] = new[] { "A delivery is paid in cash, by an M-Pesa code, by a till payment or on credit." };
            var (lines, bundles) = ValidateItems(request.Lines, request.Bundles, errors);
            if (errors.Count > 0)
                throw new ValidationException(errors);

            var branchId = _branches.RequireActiveBranchId();
            // Out with a rider at once (checkout with delivery by rider): on a new trip, or one still out with the same rider.
            var dispatch = request.RiderName is not null || request.DeliveryTripId.HasValue;
            DeliveryTrip? trip = null, newTrip = null;
            if (dispatch)
            {
                if (!_currentUser.HasPermission(DispatchPermission))
                    throw new ForbiddenException("You cannot send orders out with a rider. Take the order and ask someone who can to dispatch it.");
                (trip, newTrip) = await ResolveTripAsync(request.RiderName, request.DeliveryTripId, branchId);
            }

            // Priced and checked exactly as a sale to this customer would be: the products must be sellable in the units given, and with
            // overselling off the stock not already held for other orders must cover it.
            var preview = await _sales.PreviewSaleAsync(new CreateSaleRequestDto(customer.CustomerId, PaymentMethod.Cash, null,
                lines.Select(l => new CreateSaleLineRequestDto(l.ProductId, l.UnitOfMeasureId, l.Quantity)), null, null, null,
                Bundles: bundles.Select(b => new CreateSaleBundleRequestDto(b.ProductBundleId, b.Quantity))));
            if (preview.RequiresPrescription)
                throw new InvalidAssignmentException("Prescription-only products cannot be ordered for delivery.");
            var business = await _businessRepository.GetBusinessByIdAsync(businessId)
                ?? throw new NotFoundException("Business not found.");
            var previewLines = preview.Lines.ToList();
            var shortProducts = previewLines.GroupBy(l => l.ProductId)
                .Where(g => g.Any(l => !l.StockSufficient && (!business.AllowNegativeStock || l.IsBatchTracked)))
                .Select(g => $"'{g.First().ProductName}' ({Math.Max(0m, g.First().AvailableInBaseUnits):0.####} available, {g.Sum(l => l.RequiredInBaseUnits):0.####} needed)")
                .ToList();
            if (shortProducts.Count > 0)
                throw new InsufficientStockException($"Not enough stock for this order: {string.Join(", ", shortProducts)}.");

            var now = DateTime.UtcNow;
            var order = new DeliveryOrder
            {
                DeliveryOrderId = Guid.NewGuid(),
                OrderNumber = $"DEL-{await _repository.NextNumberAsync():D6}",
                BranchId = branchId,
                CustomerId = customer.CustomerId,
                DeliveryAddress = request.DeliveryAddress.Trim(),
                ContactPhone = string.IsNullOrWhiteSpace(request.ContactPhone) ? customer.PhoneNumber : PhoneNumberHelper.NormalizePhoneNumber(request.ContactPhone),
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                EstimatedTotal = preview.TotalAmount,
                ExpectedPaymentMethod = request.ExpectedPaymentMethod ?? PaymentMethod.Cash,
                CreatedBy = _currentUser.UserId,
                // Each line of the preview, bundle parts included, reserves what it needs.
                Lines = previewLines.Select(l => new DeliveryOrderLine
                {
                    DeliveryOrderLineId = Guid.NewGuid(), ProductId = l.ProductId, UnitOfMeasureId = l.UnitOfMeasureId,
                    Quantity = l.Quantity, QuantityInBaseUnits = l.RequiredInBaseUnits,
                    ProductBundleId = l.ProductBundleId, BundleQuantity = l.BundleQuantity
                }).ToList()
            };
            if (trip is not null)
            {
                order.Status = DeliveryOrderStatus.OutForDelivery;
                order.RiderName = trip.RiderName;
                order.DeliveryTripId = trip.DeliveryTripId;
                order.DispatchedAt = now;
                order.DispatchedBy = _currentUser.UserId;
            }

            _events.Record(new SystemEventDraft(SystemEventTypes.DeliveryOrderCreated,
                $"{order.OrderNumber} for {customer.CustomerName}: {order.Lines.Count} line(s), about {order.EstimatedTotal:0.00} " +
                $"({order.ExpectedPaymentMethod}), to {order.DeliveryAddress}.",
                EntityType: "DeliveryOrder", EntityId: order.DeliveryOrderId, BranchId: order.BranchId));
            if (trip is not null)
                _events.Record(new SystemEventDraft(SystemEventTypes.DeliveryOrderDispatched,
                    $"{order.OrderNumber} went out with {trip.RiderName} on {trip.TripNumber}.",
                    EntityType: "DeliveryOrder", EntityId: order.DeliveryOrderId, BranchId: order.BranchId,
                    Data: new { trip.DeliveryTripId, trip.TripNumber, trip.RiderName }));
            await _repository.CreateAsync(order, newTrip);
            return await GetAsync(order.DeliveryOrderId);
        }

        public async Task<PagedResult<DeliveryOrderDto>> GetOrdersAsync(int page, int pageSize, DeliveryOrderQuery query)
        {
            _tenant.RequireBusinessId();
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            ReportPeriod.ValidateOffset(query.UtcOffsetMinutes);
            if (query.From.HasValue && query.To.HasValue && query.From > query.To)
                throw new ValidationException("from", "'from' cannot be after 'to'.");
            var offset = TimeSpan.FromMinutes(query.UtcOffsetMinutes);
            var (items, total) = await _repository.GetOrdersAsync(page, pageSize, query,
                query.From is { } f ? ReportPeriod.StartOfLocalDayUtc(f, offset) : null,
                query.To is { } t ? ReportPeriod.StartOfLocalDayUtc(t.AddDays(1), offset) : null);
            return new PagedResult<DeliveryOrderDto>(items.Select(ToDto), page, pageSize, total);
        }

        public async Task<DeliveryOrderDto> GetAsync(Guid deliveryOrderId) => ToDto(await RequireAsync(deliveryOrderId));

        public async Task<DeliveryOrderDto> DispatchAsync(Guid deliveryOrderId, DispatchDeliveryOrderRequestDto request)
        {
            await DispatchManyAsync(request.RiderName, new[] { deliveryOrderId }, request.DeliveryTripId);
            return await GetAsync(deliveryOrderId);
        }

        /// <summary>Sends several pending orders out with one rider, as one trip (or adds them to a trip still out).</summary>
        public async Task<DeliveryTripDto> DispatchTripAsync(DispatchTripRequestDto request)
        {
            var ids = (request.DeliveryOrderIds ?? Enumerable.Empty<Guid>()).Distinct().ToList();
            if (ids.Count == 0)
                throw new ValidationException("deliveryOrderIds", "Choose the orders the rider is taking.");
            if (ids.Count > MaxOrdersPerTrip)
                throw new ValidationException("deliveryOrderIds", $"A trip takes at most {MaxOrdersPerTrip} orders.");
            var trip = await DispatchManyAsync(request.RiderName, ids, request.DeliveryTripId);
            return await GetTripAsync(trip.DeliveryTripId);
        }

        private async Task<DeliveryTrip> DispatchManyAsync(string? riderName, IReadOnlyList<Guid> ids, Guid? deliveryTripId)
        {
            _tenant.RequireBusinessId();
            var orders = await _repository.GetManyAsync(ids);
            var missing = ids.Except(orders.Select(o => o.DeliveryOrderId)).ToList();
            if (missing.Count > 0)
                throw new NotFoundException($"Delivery order '{missing[0]}' not found.");
            var notPending = orders.Where(o => o.Status != DeliveryOrderStatus.Pending).ToList();
            if (notPending.Count > 0)
                throw new InvalidAssignmentException(
                    $"{string.Join(", ", notPending.Select(o => $"{o.OrderNumber} is {o.Status}"))}: only a pending order can be dispatched.");
            var branchId = orders[0].BranchId;
            if (orders.Any(o => o.BranchId != branchId))
                throw new ValidationException("deliveryOrderIds", "A trip leaves from one branch: these orders belong to different branches.");

            var (trip, newTrip) = await ResolveTripAsync(riderName, deliveryTripId, branchId);
            foreach (var order in orders)
                _events.Record(new SystemEventDraft(SystemEventTypes.DeliveryOrderDispatched, $"{order.OrderNumber} went out with {trip.RiderName} on {trip.TripNumber}.",
                    EntityType: "DeliveryOrder", EntityId: order.DeliveryOrderId, BranchId: order.BranchId,
                    Data: new { trip.DeliveryTripId, trip.TripNumber, trip.RiderName }));
            if (!await _repository.DispatchAsync(ids, trip, newTrip is not null, _currentUser.UserId))
                throw new InvalidAssignmentException("Someone else has just dispatched or cancelled one of these orders. Reload them.");
            return trip;
        }

        /// <summary>The trip the orders go out on: a new one for <paramref name="riderName"/>, or <paramref name="deliveryTripId"/> while it is
        /// still out. NewTrip is set when it has to be saved.</summary>
        private async Task<(DeliveryTrip Trip, DeliveryTrip? NewTrip)> ResolveTripAsync(string? riderName, Guid? deliveryTripId, Guid branchId)
        {
            var rider = string.IsNullOrWhiteSpace(riderName) ? null : riderName.Trim();
            if (rider is { Length: > MaxRiderNameLength })
                throw new ValidationException("riderName", $"The rider's name is at most {MaxRiderNameLength} characters.");

            if (deliveryTripId is { } tripId)
            {
                var existing = await _repository.GetTripAsync(tripId)
                    ?? throw new NotFoundException($"Delivery trip '{tripId}' not found.");
                if (existing.BranchId != branchId)
                    throw new ValidationException("deliveryTripId", $"{existing.TripNumber} left from another branch.");
                if (rider is not null && !SameRider(rider, existing.RiderName))
                    throw new ValidationException("riderName", $"{existing.TripNumber} is {existing.RiderName}'s trip.");
                if (!existing.Orders.Any(o => o.Status == DeliveryOrderStatus.OutForDelivery))
                    throw new InvalidAssignmentException($"{existing.TripNumber} is already back: start a new trip.");
                return (existing, null);
            }

            if (rider is null)
                throw new ValidationException("riderName", "Who is taking it? The rider's name is required.");
            var trip = new DeliveryTrip
            {
                DeliveryTripId = Guid.NewGuid(),
                TripNumber = $"TRP-{await _repository.NextTripNumberAsync():D6}",
                BranchId = branchId,
                RiderName = rider,
                DispatchedAt = DateTime.UtcNow,
                DispatchedBy = _currentUser.UserId
            };
            return (trip, trip);
        }

        public async Task<DeliveryOrderDto> DeliverAsync(Guid deliveryOrderId, DeliverDeliveryOrderRequestDto request)
        {
            var order = await RequireAsync(deliveryOrderId);
            if (order.Status != DeliveryOrderStatus.OutForDelivery)
                throw new InvalidAssignmentException(order.Status == DeliveryOrderStatus.Pending
                    ? $"{order.OrderNumber} has not gone out yet: dispatch it with the rider's name first."
                    : $"{order.OrderNumber} is {order.Status}: only an order out for delivery can be delivered.");
            if (order.BranchId != _branches.RequireActiveBranchId())
                throw new ValidationException("branchId", $"{order.OrderNumber} belongs to {order.Branch.KraBranchId} {order.Branch.Name}: switch to that branch to record it.");

            if (!IsDeliveryPayment(request.PaymentMethod))
                throw new ValidationException("paymentMethod",
                    "A delivery is paid in cash, by a till payment (pick it from the unmatched payments), by an M-Pesa code, or on credit.");
            // Paid by M-Pesa: the rider brings back the code from the customer's SMS (or the till payment is picked). Without it the
            // sale is not completed.
            if (request.PaymentMethod == PaymentMethod.MpesaReference && string.IsNullOrWhiteSpace(request.MpesaReference))
                throw new ValidationException("mpesaReference", "Paid by M-Pesa: enter the M-Pesa code the rider brought back to complete the sale.");
            // On credit, the sale is charged to the customer's account like any credit sale. A promised date is optional.
            if (request.PaymentMethod == PaymentMethod.Credit)
            {
                if (!_currentUser.HasPermission(AcceptCreditPermission))
                    throw new ForbiddenException("Only a manager can accept a delivery on credit. Ask them to record this delivery.");
            }
            else if (request.PaymentDueDate.HasValue)
                throw new ValidationException("paymentDueDate", "A promised payment date applies only to a delivery on credit.");
            if (request.Notes is { Length: > MaxTextLength })
                throw new ValidationException("notes", $"Notes are at most {MaxTextLength} characters.");

            var errors = new Dictionary<string, string[]>();
            List<DeliveryOrderLineRequestDto> lines;
            List<DeliveryOrderBundleRequestDto> bundles;
            if (request.Lines is null && request.Bundles is null)
            {
                lines = order.Lines.Where(l => l.ProductBundleId is null)
                    .Select(l => new DeliveryOrderLineRequestDto(l.ProductId, l.UnitOfMeasureId, l.Quantity)).ToList();
                bundles = order.Lines.Where(l => l.ProductBundleId is not null).GroupBy(l => l.ProductBundleId!.Value)
                    .Select(g => new DeliveryOrderBundleRequestDto(g.Key, g.First().BundleQuantity ?? 1m)).ToList();
            }
            else
                (lines, bundles) = ValidateItems(request.Lines, request.Bundles, errors);
            if (errors.Count > 0)
                throw new ValidationException(errors);

            var note = $"Delivery {order.OrderNumber} by {order.RiderName}" + (string.IsNullOrWhiteSpace(request.Notes) ? "" : $" — {request.Notes.Trim()}");
            var sale = await _sales.RecordDeliverySaleAsync(new CreateSaleRequestDto(order.CustomerId, request.PaymentMethod, note,
                lines.Select(l => new CreateSaleLineRequestDto(l.ProductId, l.UnitOfMeasureId, l.Quantity)), request.AmountTendered, null,
                request.MpesaTransactionId, OverrideCreditLimit: request.OverrideCreditLimit, PaymentDueDate: request.PaymentDueDate,
                Bundles: bundles.Select(b => new CreateSaleBundleRequestDto(b.ProductBundleId, b.Quantity)),
                MpesaReference: request.MpesaReference), deliveryOrderId);

            // The sale (and the order's move to Delivered) has already been saved, so this event has no change of its own to ride on.
            await _events.RecordNowAsync(new SystemEventDraft(SystemEventTypes.DeliveryOrderDelivered,
                $"{order.OrderNumber} was delivered to {order.Customer.CustomerName} by {order.RiderName}: {sale.SaleNumber}, {sale.TotalAmount:0.00} " +
                (request.PaymentMethod != PaymentMethod.Credit ? $"by {request.PaymentMethod}."
                    : request.PaymentDueDate is { } due ? $"on credit, promised by {due:yyyy-MM-dd}." : "on credit."),
                EntityType: "DeliveryOrder", EntityId: deliveryOrderId, BranchId: order.BranchId,
                Data: new { sale.SaleId, sale.SaleNumber, sale.TotalAmount, request.PaymentMethod, request.PaymentDueDate, order.RiderName, sale.MpesaReference }));
            return await GetAsync(deliveryOrderId);
        }

        public async Task<DeliveryOrderDto> CancelAsync(Guid deliveryOrderId, CancelDeliveryOrderRequestDto request)
        {
            var order = await RequireAsync(deliveryOrderId);
            var reason = RequireReason(request.Reason, "Why is it cancelled? A reason is required (e.g. customer called it off).");

            _events.Record(new SystemEventDraft(SystemEventTypes.DeliveryOrderCancelled, $"{order.OrderNumber} was cancelled: {reason}",
                EntityType: "DeliveryOrder", EntityId: deliveryOrderId, BranchId: order.BranchId));
            if (!await _repository.CancelAsync(deliveryOrderId, reason, _currentUser.UserId))
                throw new InvalidAssignmentException($"{order.OrderNumber} is {(await RequireAsync(deliveryOrderId)).Status}: only an open order can be cancelled.");
            return await GetAsync(deliveryOrderId);
        }

        /// <summary>
        /// The rider is back with the goods undelivered. No sale is made and nothing had left stock, so the reservation is simply released
        /// and the water is sellable again. (Part of an order returned: record the delivery with the lines actually handed over.)
        /// </summary>
        public async Task<DeliveryOrderDto> ReturnAsync(Guid deliveryOrderId, ReturnDeliveryOrderRequestDto request)
        {
            var order = await RequireAsync(deliveryOrderId);
            var reason = RequireReason(request.Reason, "Why did it come back? A reason is required (e.g. customer not home).");
            if (order.Status != DeliveryOrderStatus.OutForDelivery)
                throw new InvalidAssignmentException(order.Status == DeliveryOrderStatus.Pending
                    ? $"{order.OrderNumber} has not gone out yet: cancel it instead."
                    : $"{order.OrderNumber} is {order.Status}: only an order out for delivery can come back.");

            _events.Record(new SystemEventDraft(SystemEventTypes.DeliveryOrderReturned,
                $"{order.OrderNumber} came back undelivered with {order.RiderName}: {reason}",
                EventSeverity.Warning, "DeliveryOrder", deliveryOrderId, new { order.RiderName, order.EstimatedTotal, reason }, BranchId: order.BranchId));
            if (!await _repository.ReturnAsync(deliveryOrderId, reason, _currentUser.UserId))
                throw new InvalidAssignmentException($"{order.OrderNumber} is {(await RequireAsync(deliveryOrderId)).Status}: only an order out for delivery can come back.");
            return await GetAsync(deliveryOrderId);
        }

        public async Task<DeliverySummaryDto> GetSummaryAsync(DateOnly from, DateOnly to, int utcOffsetMinutes)
        {
            _tenant.RequireBusinessId();
            var period = ReportPeriod.Create(from, to, utcOffsetMinutes);
            var orders = await _repository.GetForSummaryAsync(period.FromUtc, period.ToUtcExclusive);
            var trips = await _repository.GetTripsAsync(period.FromUtc, period.ToUtcExclusive, null);

            bool InPeriod(DateTime? at) => at is { } t && t >= period.FromUtc && t < period.ToUtcExclusive;
            var delivered = orders.Where(o => o.Status == DeliveryOrderStatus.Delivered && InPeriod(o.DeliveredAt) && o.Sale is not null).ToList();
            var returned = orders.Where(o => o.Status == DeliveryOrderStatus.Returned && InPeriod(o.ReturnedAt)).ToList();
            var outNow = orders.Where(o => o.Status == DeliveryOrderStatus.OutForDelivery).ToList();
            decimal Paid(IEnumerable<DeliveryOrder> list, PaymentMethod method) => list.Where(o => o.Sale!.PaymentMethod == method).Sum(o => o.Sale!.TotalAmount);
            // The rider hands in cash only: a till payment and a typed M-Pesa code both went straight to the business's M-Pesa.
            decimal PaidByMpesa(IEnumerable<DeliveryOrder> list) => Paid(list, PaymentMethod.MpesaTill) + Paid(list, PaymentMethod.MpesaReference);

            var riders = delivered.Concat(outNow).Concat(returned).Select(o => o.RiderName ?? string.Empty)
                .Concat(trips.Select(t => t.RiderName))
                .GroupBy(RiderKey).Select(g => g.First());
            var byRider = riders.Select(rider =>
                {
                    bool Theirs(string? name) => RiderKey(name ?? string.Empty) == RiderKey(rider);
                    var theirs = delivered.Where(o => Theirs(o.RiderName)).ToList();
                    var theirsOut = outNow.Where(o => Theirs(o.RiderName)).ToList();
                    return new RiderSummaryDto(rider, theirs.Count, theirs.Sum(o => o.Sale!.TotalAmount), Paid(theirs, PaymentMethod.Cash),
                        PaidByMpesa(theirs), Paid(theirs, PaymentMethod.Credit), theirsOut.Count,
                        returned.Count(o => Theirs(o.RiderName)), trips.Count(t => Theirs(t.RiderName)),
                        theirsOut.Where(o => (o.ExpectedPaymentMethod ?? PaymentMethod.Cash) == PaymentMethod.Cash).Sum(o => o.EstimatedTotal));
                })
                .OrderByDescending(r => r.DeliveredTotal).ThenBy(r => r.RiderName)
                .ToList();

            return new DeliverySummaryDto(period.From, period.To, delivered.Count, delivered.Sum(o => o.Sale!.TotalAmount),
                Paid(delivered, PaymentMethod.Cash), PaidByMpesa(delivered), Paid(delivered, PaymentMethod.Credit),
                orders.Count(o => o.Status == DeliveryOrderStatus.Cancelled && InPeriod(o.CancelledAt)),
                orders.Count(o => o.Status == DeliveryOrderStatus.Pending), outNow.Count, byRider, returned.Count, trips.Count);
        }

        // ---- Trips and fuel ----

        public async Task<IEnumerable<DeliveryTripDto>> GetTripsAsync(DeliveryTripQuery query)
        {
            _tenant.RequireBusinessId();
            var period = ReportPeriod.Create(query.From, query.To, query.UtcOffsetMinutes);
            var trips = await _repository.GetTripsAsync(period.FromUtc, period.ToUtcExclusive,
                string.IsNullOrWhiteSpace(query.RiderName) ? null : query.RiderName.Trim());
            return trips.OrderByDescending(t => t.DispatchedAt).Select(ToDto).ToList();
        }

        public async Task<DeliveryTripDto> GetTripAsync(Guid deliveryTripId)
        {
            _tenant.RequireBusinessId();
            return ToDto(await _repository.GetTripAsync(deliveryTripId)
                ?? throw new NotFoundException($"Delivery trip '{deliveryTripId}' not found."));
        }

        /// <summary>
        /// Trips per rider in the period against the fuel paid for them (expenses tagged with the rider). With an expected cost per trip,
        /// each rider's fuel is checked against what their trips account for; without one, against the business's own average per trip.
        /// Wasted trips (nothing delivered) and single-order trips show where trips could have been avoided or combined.
        /// </summary>
        public async Task<DeliveryFuelReportDto> GetFuelReportAsync(DateOnly from, DateOnly to, Guid? expenseCategoryId, decimal? expectedFuelPerTrip,
            int utcOffsetMinutes)
        {
            _tenant.RequireBusinessId();
            if (expectedFuelPerTrip is < 0)
                throw new ValidationException("expectedFuelPerTrip", "The expected fuel cost per trip cannot be negative.");
            var period = ReportPeriod.Create(from, to, utcOffsetMinutes);
            var trips = await _repository.GetTripsAsync(period.FromUtc, period.ToUtcExclusive, null);
            var fuel = await _repository.GetRiderExpensesAsync(period.From, period.To, expenseCategoryId);

            var totalTrips = trips.Count;
            var totalFuel = fuel.Sum(f => f.Amount);
            decimal? average = totalTrips > 0 ? RoundMoney(totalFuel / totalTrips) : null;

            var riders = trips.Select(t => t.RiderName).Concat(fuel.Select(f => f.RiderName)).GroupBy(RiderKey).Select(g => g.First());
            var byRider = riders.Select(rider =>
                {
                    var key = RiderKey(rider);
                    var theirTrips = trips.Where(t => RiderKey(t.RiderName) == key).ToList();
                    var theirFuel = fuel.Where(f => RiderKey(f.RiderName) == key).ToList();
                    var orders = theirTrips.SelectMany(t => t.Orders).ToList();
                    var spent = theirFuel.Sum(f => f.Amount);
                    decimal? perTrip = theirTrips.Count > 0 ? RoundMoney(spent / theirTrips.Count) : null;
                    decimal? expected = expectedFuelPerTrip is { } e ? RoundMoney(e * theirTrips.Count) : null;
                    decimal? variance = expected is { } x ? spent - x : null;
                    // Fuel with no trip to show for it never accounts for itself.
                    var accountedFor = variance is { } v ? v <= 0 : theirTrips.Count > 0 || spent == 0;
                    var aboveAverage = average is > 0 && (perTrip is null ? spent > 0 : perTrip > average * AboveAverageFactor);
                    return new RiderFuelDto(rider, theirTrips.Count, orders.Count, orders.Count(o => o.Status == DeliveryOrderStatus.Delivered),
                        orders.Count(o => o.Status == DeliveryOrderStatus.Returned), theirTrips.Count(IsWasted), theirTrips.Count(t => t.Orders.Count == 1),
                        spent, theirFuel.Count, perTrip, expected, variance, accountedFor, aboveAverage);
                })
                .OrderByDescending(r => r.FuelVariance ?? r.FuelSpent).ThenBy(r => r.RiderName)
                .ToList();

            decimal? expectedTotal = expectedFuelPerTrip is { } ept ? RoundMoney(ept * totalTrips) : null;
            return new DeliveryFuelReportDto(period.From, period.To, expenseCategoryId, expectedFuelPerTrip, totalTrips, trips.Count(IsWasted),
                totalFuel, average, expectedTotal, expectedTotal is { } et ? totalFuel - et : null, byRider);
        }

        // ---- Helpers ----

        private static bool IsDeliveryPayment(PaymentMethod method) =>
            method is PaymentMethod.Cash or PaymentMethod.MpesaTill or PaymentMethod.MpesaReference or PaymentMethod.Credit;

        /// <summary>Riders are names typed at the till: "Peter", "peter " and "PETER" are one rider.</summary>
        private static string RiderKey(string name) => name.Trim().ToUpperInvariant();
        private static bool SameRider(string a, string b) => RiderKey(a) == RiderKey(b);

        private static bool IsBack(DeliveryTrip t) => t.Orders.All(o => !o.IsOpen);
        private static bool IsWasted(DeliveryTrip t) => t.Orders.Count > 0 && IsBack(t) && t.Orders.All(o => o.Status != DeliveryOrderStatus.Delivered);

        private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private static string RequireReason(string? given, string missingMessage)
        {
            if (string.IsNullOrWhiteSpace(given))
                throw new ValidationException("reason", missingMessage);
            var reason = given.Trim();
            if (reason.Length > MaxTextLength)
                throw new ValidationException("reason", $"The reason is at most {MaxTextLength} characters.");
            return reason;
        }

        private static (List<DeliveryOrderLineRequestDto> Lines, List<DeliveryOrderBundleRequestDto> Bundles) ValidateItems(
            IEnumerable<DeliveryOrderLineRequestDto>? givenLines, IEnumerable<DeliveryOrderBundleRequestDto>? givenBundles,
            Dictionary<string, string[]> errors)
        {
            var lines = (givenLines ?? Enumerable.Empty<DeliveryOrderLineRequestDto>()).ToList();
            var bundles = (givenBundles ?? Enumerable.Empty<DeliveryOrderBundleRequestDto>()).ToList();
            if (lines.Count == 0 && bundles.Count == 0)
                errors["lines"] = new[] { "An order needs at least one line or bundle." };
            else if (lines.Any(l => l.Quantity <= 0))
                errors["lines"] = new[] { "Quantity must be greater than zero on every line." };
            else if (lines.GroupBy(l => (l.ProductId, l.UnitOfMeasureId)).Any(g => g.Count() > 1))
                errors["lines"] = new[] { "Each product and unit can appear only once — combine the lines." };
            if (bundles.Any(b => b.Quantity <= 0 || b.Quantity != decimal.Truncate(b.Quantity)))
                errors["bundles"] = new[] { "Bundles are sold whole: each quantity must be a whole number greater than zero." };
            else if (bundles.GroupBy(b => b.ProductBundleId).Any(g => g.Count() > 1))
                errors["bundles"] = new[] { "Each bundle can appear only once — combine the quantities." };
            return (lines, bundles);
        }

        private async Task<DeliveryOrder> RequireAsync(Guid deliveryOrderId)
        {
            _tenant.RequireBusinessId();
            return await _repository.GetAsync(deliveryOrderId)
                ?? throw new NotFoundException($"Delivery order '{deliveryOrderId}' not found.");
        }

        private static DeliveryOrderDto ToDto(DeliveryOrder o) =>
            new(o.DeliveryOrderId, o.OrderNumber, o.Status, o.CustomerId, o.Customer?.CustomerName ?? string.Empty, o.DeliveryAddress, o.ContactPhone,
                o.Notes, o.EstimatedTotal, o.RiderName, o.DispatchedAt, o.SaleId, o.Sale?.SaleNumber, o.Sale?.TotalAmount, o.Sale?.PaymentMethod,
                o.Sale?.PaymentDueDate, o.DeliveredAt, o.CancelledAt, o.CancelReason, o.CreatedAt, o.CreatedBy, o.BranchId,
                o.Branch is null ? null : Branch.FormatKraBranchId(o.Branch.BranchNumber), o.Branch?.Name,
                o.Lines.Select(l => new DeliveryOrderLineDto(l.DeliveryOrderLineId, l.ProductId, l.Product?.Name ?? string.Empty, l.Product?.Sku ?? string.Empty,
                    l.UnitOfMeasureId, l.UnitOfMeasure?.Symbol, l.Quantity, l.QuantityInBaseUnits, l.ProductBundleId, l.ProductBundle?.Name,
                    l.BundleQuantity)).ToList(),
                o.ExpectedPaymentMethod, o.DeliveryTripId, o.Trip?.TripNumber, o.ReturnedAt, o.ReturnReason);

        private static DeliveryTripDto ToDto(DeliveryTrip t)
        {
            var orders = t.Orders.OrderBy(o => o.OrderNumber).ToList();
            return new DeliveryTripDto(t.DeliveryTripId, t.TripNumber, t.RiderName, t.DispatchedAt, t.BranchId, orders.Count,
                orders.Count(o => o.Status == DeliveryOrderStatus.Delivered), orders.Count(o => o.Status == DeliveryOrderStatus.Returned),
                orders.Count(o => o.Status == DeliveryOrderStatus.OutForDelivery),
                orders.Where(o => o.Status == DeliveryOrderStatus.Delivered).Sum(o => o.Sale?.TotalAmount ?? 0m), IsBack(t), IsWasted(t),
                orders.Select(o => new DeliveryTripOrderDto(o.DeliveryOrderId, o.OrderNumber, o.Status, o.Customer?.CustomerName ?? string.Empty,
                    o.DeliveryAddress, o.EstimatedTotal, o.Sale?.SaleNumber, o.Sale?.TotalAmount)).ToList());
        }
    }
}
