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
    /// <param name="ContactPhone">Who to call on arrival. Defaults to the customer's phone.</param>
    public record CreateDeliveryOrderRequestDto(Guid CustomerId, string DeliveryAddress, string? ContactPhone, string? Notes,
        IEnumerable<DeliveryOrderLineRequestDto> Lines);
    public record DispatchDeliveryOrderRequestDto(string RiderName);
    /// <summary>
    /// Recorded when the customer has received the goods (typically by the cashier once the rider is back). Payment is Cash
    /// (AmountTendered), MpesaTill (MpesaTransactionId of the till payment), MpesaReference (the M-Pesa code from the customer's SMS) or
    /// Credit — a promise to pay by PaymentDueDate, which needs DeliveryOrders.AcceptCredit.
    /// </summary>
    /// <param name="Lines">What was actually handed over, when it differs from the order (say the customer had a container after all).
    /// Left out = exactly as ordered. The sale is priced from these.</param>
    public record DeliverDeliveryOrderRequestDto(PaymentMethod PaymentMethod, decimal? AmountTendered = null, Guid? MpesaTransactionId = null,
        DateOnly? PaymentDueDate = null, bool OverrideCreditLimit = false, IEnumerable<DeliveryOrderLineRequestDto>? Lines = null, string? Notes = null,
        string? MpesaReference = null);
    public record CancelDeliveryOrderRequestDto(string Reason);

    public record DeliveryOrderLineDto(Guid DeliveryOrderLineId, Guid ProductId, string ProductName, string Sku, Guid UnitOfMeasureId,
        string? UnitSymbol, decimal Quantity, decimal QuantityInBaseUnits);
    /// <param name="EstimatedTotal">At the customer's prices when the order was taken. The sale (SaleTotal) is what was charged.</param>
    public record DeliveryOrderDto(Guid DeliveryOrderId, string OrderNumber, DeliveryOrderStatus Status, Guid CustomerId, string CustomerName,
        string DeliveryAddress, string? ContactPhone, string? Notes, decimal EstimatedTotal,
        string? RiderName, DateTime? DispatchedAt,
        Guid? SaleId, string? SaleNumber, decimal? SaleTotal, PaymentMethod? PaymentMethod, DateOnly? PaymentDueDate, DateTime? DeliveredAt,
        DateTime? CancelledAt, string? CancelReason, DateTime CreatedAt, Guid? CreatedBy,
        Guid BranchId, string? KraBranchId, string? BranchName, IEnumerable<DeliveryOrderLineDto> Lines);

    /// <summary>Filters for the list. from/to are local days the order was TAKEN, inclusive.</summary>
    public record DeliveryOrderQuery(DeliveryOrderStatus? Status = null, Guid? CustomerId = null, string? RiderName = null,
        DateOnly? From = null, DateOnly? To = null, int UtcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes);

    /// <summary>
    /// What each rider delivered in the period (by delivery day) and how it was paid — CashCollected is the cash they should hand in.
    /// PaidByMpesaTill is everything paid by M-Pesa: a matched till payment or a typed M-Pesa code. OutForDeliveryNow is as of now, whatever the period.
    /// </summary>
    public record RiderSummaryDto(string RiderName, int DeliveredCount, decimal DeliveredTotal, decimal CashCollected, decimal PaidByMpesaTill,
        decimal OnCredit, int OutForDeliveryNow);
    public record DeliverySummaryDto(DateOnly From, DateOnly To, int DeliveredCount, decimal DeliveredTotal, decimal CashCollected,
        decimal PaidByMpesaTill, decimal OnCredit, int CancelledCount, int PendingNow, int OutForDeliveryNow, IEnumerable<RiderSummaryDto> ByRider);

    public interface IDeliveryOrderRepository
    {
        Task<int> NextNumberAsync();
        Task CreateAsync(DeliveryOrder order);
        /// <summary>With its customer, branch, lines and (once delivered) sale.</summary>
        Task<DeliveryOrder?> GetAsync(Guid deliveryOrderId);
        Task<(IReadOnlyList<DeliveryOrder> Items, int TotalCount)> GetOrdersAsync(int page, int pageSize, DeliveryOrderQuery query,
            DateTime? fromUtc, DateTime? toUtcExclusive);
        /// <summary>Pending → OutForDelivery, in one conditional update that commits with the events recorded for it. False (and nothing
        /// saved) when it was no longer pending.</summary>
        Task<bool> DispatchAsync(Guid deliveryOrderId, string riderName, Guid? userId);
        /// <summary>Pending or OutForDelivery → Cancelled, in one conditional update that commits with the events recorded for it. False
        /// (and nothing saved) when it was no longer open.</summary>
        Task<bool> CancelAsync(Guid deliveryOrderId, string reason, Guid? userId);
        /// <summary>Orders delivered or cancelled in the window, and every order open now, with their sales.</summary>
        Task<IReadOnlyList<DeliveryOrder>> GetForSummaryAsync(DateTime fromUtc, DateTime toUtcExclusive);
    }

    /// <summary>
    /// Orders taken for delivery: Pending (stock reserved) → OutForDelivery (with a rider) → Delivered (an ordinary sale, paid by cash, a
    /// till payment or on credit), or Cancelled. Nothing here moves stock or money itself: the sale does, once, when the order is delivered.
    /// </summary>
    public class DeliveryOrderService
    {
        public const string AcceptCreditPermission = "DeliveryOrders.AcceptCredit";
        private const int MaxAddressLength = 500;
        private const int MaxRiderNameLength = 100;
        private const int MaxTextLength = 1000;

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
            var lines = ValidateLines(request.Lines, errors);
            if (errors.Count > 0)
                throw new ValidationException(errors);

            // Priced and checked exactly as a sale to this customer would be: the products must be sellable in the units given, and with
            // overselling off the stock not already held for other orders must cover it.
            var preview = await _sales.PreviewSaleAsync(new CreateSaleRequestDto(customer.CustomerId, PaymentMethod.Cash, null,
                lines.Select(l => new CreateSaleLineRequestDto(l.ProductId, l.UnitOfMeasureId, l.Quantity)), null, null, null));
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

            var order = new DeliveryOrder
            {
                DeliveryOrderId = Guid.NewGuid(),
                OrderNumber = $"DEL-{await _repository.NextNumberAsync():D6}",
                BranchId = _branches.RequireActiveBranchId(),
                CustomerId = customer.CustomerId,
                DeliveryAddress = request.DeliveryAddress.Trim(),
                ContactPhone = string.IsNullOrWhiteSpace(request.ContactPhone) ? customer.PhoneNumber : PhoneNumberHelper.NormalizePhoneNumber(request.ContactPhone),
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                EstimatedTotal = preview.TotalAmount,
                CreatedBy = _currentUser.UserId,
                Lines = lines.Select((l, i) => new DeliveryOrderLine
                {
                    DeliveryOrderLineId = Guid.NewGuid(), ProductId = l.ProductId, UnitOfMeasureId = l.UnitOfMeasureId,
                    Quantity = l.Quantity, QuantityInBaseUnits = previewLines[i].RequiredInBaseUnits
                }).ToList()
            };

            _events.Record(new SystemEventDraft(SystemEventTypes.DeliveryOrderCreated,
                $"{order.OrderNumber} for {customer.CustomerName}: {order.Lines.Count} line(s), about {order.EstimatedTotal:0.00}, to {order.DeliveryAddress}.",
                EntityType: "DeliveryOrder", EntityId: order.DeliveryOrderId, BranchId: order.BranchId));
            await _repository.CreateAsync(order);
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
            var order = await RequireAsync(deliveryOrderId);
            if (string.IsNullOrWhiteSpace(request.RiderName))
                throw new ValidationException("riderName", "Who is taking it? The rider's name is required.");
            var rider = request.RiderName.Trim();
            if (rider.Length > MaxRiderNameLength)
                throw new ValidationException("riderName", $"The rider's name is at most {MaxRiderNameLength} characters.");

            _events.Record(new SystemEventDraft(SystemEventTypes.DeliveryOrderDispatched, $"{order.OrderNumber} went out with {rider}.",
                EntityType: "DeliveryOrder", EntityId: deliveryOrderId, BranchId: order.BranchId));
            if (!await _repository.DispatchAsync(deliveryOrderId, rider, _currentUser.UserId))
                throw new InvalidAssignmentException($"{order.OrderNumber} is {(await RequireAsync(deliveryOrderId)).Status}: only a pending order can be dispatched.");
            return await GetAsync(deliveryOrderId);
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

            if (request.PaymentMethod is not (PaymentMethod.Cash or PaymentMethod.MpesaTill or PaymentMethod.MpesaReference or PaymentMethod.Credit))
                throw new ValidationException("paymentMethod",
                    "A delivery is paid in cash, by a till payment (pick it from the unmatched payments), by an M-Pesa code, or on credit.");
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
            var lines = request.Lines is null
                ? order.Lines.Select(l => new DeliveryOrderLineRequestDto(l.ProductId, l.UnitOfMeasureId, l.Quantity)).ToList()
                : ValidateLines(request.Lines, errors);
            if (errors.Count > 0)
                throw new ValidationException(errors);

            var note = $"Delivery {order.OrderNumber} by {order.RiderName}" + (string.IsNullOrWhiteSpace(request.Notes) ? "" : $" — {request.Notes.Trim()}");
            var sale = await _sales.RecordDeliverySaleAsync(new CreateSaleRequestDto(order.CustomerId, request.PaymentMethod, note,
                lines.Select(l => new CreateSaleLineRequestDto(l.ProductId, l.UnitOfMeasureId, l.Quantity)), request.AmountTendered, null,
                request.MpesaTransactionId, OverrideCreditLimit: request.OverrideCreditLimit, PaymentDueDate: request.PaymentDueDate,
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
            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new ValidationException("reason", "Why is it cancelled? A reason is required (e.g. customer not reachable).");
            var reason = request.Reason.Trim();
            if (reason.Length > MaxTextLength)
                throw new ValidationException("reason", $"The reason is at most {MaxTextLength} characters.");

            _events.Record(new SystemEventDraft(SystemEventTypes.DeliveryOrderCancelled, $"{order.OrderNumber} was cancelled: {reason}",
                EntityType: "DeliveryOrder", EntityId: deliveryOrderId, BranchId: order.BranchId));
            if (!await _repository.CancelAsync(deliveryOrderId, reason, _currentUser.UserId))
                throw new InvalidAssignmentException($"{order.OrderNumber} is {(await RequireAsync(deliveryOrderId)).Status}: only an open order can be cancelled.");
            return await GetAsync(deliveryOrderId);
        }

        public async Task<DeliverySummaryDto> GetSummaryAsync(DateOnly from, DateOnly to, int utcOffsetMinutes)
        {
            _tenant.RequireBusinessId();
            var period = ReportPeriod.Create(from, to, utcOffsetMinutes);
            var orders = await _repository.GetForSummaryAsync(period.FromUtc, period.ToUtcExclusive);

            bool InPeriod(DateTime? at) => at is { } t && t >= period.FromUtc && t < period.ToUtcExclusive;
            var delivered = orders.Where(o => o.Status == DeliveryOrderStatus.Delivered && InPeriod(o.DeliveredAt) && o.Sale is not null).ToList();
            var outNow = orders.Where(o => o.Status == DeliveryOrderStatus.OutForDelivery).ToList();
            decimal Paid(IEnumerable<DeliveryOrder> list, PaymentMethod method) => list.Where(o => o.Sale!.PaymentMethod == method).Sum(o => o.Sale!.TotalAmount);
            // The rider hands in cash only: a till payment and a typed M-Pesa code both went straight to the business's M-Pesa.
            decimal PaidByMpesa(IEnumerable<DeliveryOrder> list) => Paid(list, PaymentMethod.MpesaTill) + Paid(list, PaymentMethod.MpesaReference);

            var riders = delivered.Select(o => o.RiderName ?? string.Empty).Concat(outNow.Select(o => o.RiderName ?? string.Empty)).Distinct();
            var byRider = riders.Select(rider =>
                {
                    var theirs = delivered.Where(o => (o.RiderName ?? string.Empty) == rider).ToList();
                    return new RiderSummaryDto(rider, theirs.Count, theirs.Sum(o => o.Sale!.TotalAmount), Paid(theirs, PaymentMethod.Cash),
                        PaidByMpesa(theirs), Paid(theirs, PaymentMethod.Credit), outNow.Count(o => (o.RiderName ?? string.Empty) == rider));
                })
                .OrderByDescending(r => r.DeliveredTotal).ThenBy(r => r.RiderName)
                .ToList();

            return new DeliverySummaryDto(period.From, period.To, delivered.Count, delivered.Sum(o => o.Sale!.TotalAmount),
                Paid(delivered, PaymentMethod.Cash), PaidByMpesa(delivered), Paid(delivered, PaymentMethod.Credit),
                orders.Count(o => o.Status == DeliveryOrderStatus.Cancelled && InPeriod(o.CancelledAt)),
                orders.Count(o => o.Status == DeliveryOrderStatus.Pending), outNow.Count, byRider);
        }

        private static List<DeliveryOrderLineRequestDto> ValidateLines(IEnumerable<DeliveryOrderLineRequestDto>? given, Dictionary<string, string[]> errors)
        {
            var lines = (given ?? Enumerable.Empty<DeliveryOrderLineRequestDto>()).ToList();
            if (lines.Count == 0)
                errors["lines"] = new[] { "An order needs at least one line." };
            else if (lines.Any(l => l.Quantity <= 0))
                errors["lines"] = new[] { "Quantity must be greater than zero on every line." };
            else if (lines.GroupBy(l => (l.ProductId, l.UnitOfMeasureId)).Any(g => g.Count() > 1))
                errors["lines"] = new[] { "Each product and unit can appear only once — combine the lines." };
            return lines;
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
                    l.UnitOfMeasureId, l.UnitOfMeasure?.Symbol, l.Quantity, l.QuantityInBaseUnits)).ToList());
    }
}
