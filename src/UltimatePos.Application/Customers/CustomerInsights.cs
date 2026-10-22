using UltimatePos.Application.Accounting;
using UltimatePos.Application.Business;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Notifications;
using UltimatePos.Application.Reports;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Customers
{
    /// <summary>
    /// How a customer has been buying, relative to the business's other customers in the same period. Top: among the top fifth by
    /// spend. Regular: comes back at least as often as the business's typical repeat customer. Slipping: has stayed away more than twice
    /// as long as usual for them. Top additionally needs more than one purchase, so one big order does not make a loyal customer.
    /// New: first ever purchase in the last 30 days. Occasional: the rest.
    /// </summary>
    public enum CustomerSegment { Occasional, New, Regular, Top, Slipping }

    /// <summary>What the numbers point to. The system suggests; the owner decides whether, and how much, discount to give.</summary>
    public enum CustomerSuggestedAction { None, ConsiderDiscount, WinBack, ReviewDiscount }

    public enum CustomerInsightsSort { Spend, Purchases, Recent }

    /// <param name="TotalSpent">Completed sales in the period, VAT included, less what was returned.</param>
    /// <param name="AverageDaysBetweenPurchases">Null with fewer than two purchases.</param>
    /// <param name="DiscountGiven">The standing-discount money given on their sales in the period.</param>
    /// <param name="SpendRank">1 = spent the most in the period.</param>
    public record CustomerBuyingSummaryDto(Guid CustomerId, string CustomerName, string? PhoneNumber, int PurchaseCount, decimal TotalSpent,
        decimal AverageSpend, DateTime? FirstPurchaseAt, DateTime LastPurchaseAt, int DaysSinceLastPurchase, decimal? AverageDaysBetweenPurchases,
        decimal? DiscountPercent, decimal DiscountGiven, CustomerSegment Segment, CustomerSuggestedAction SuggestedAction, int SpendRank);

    public record CustomerMonthDto(string Month, int PurchaseCount, decimal Total);
    /// <param name="QuantityInBaseUnits">Net of returns, in the product's base unit (litres of water, pieces of bottles).</param>
    public record CustomerProductDto(Guid ProductId, string ProductName, string? BaseUnitSymbol, decimal QuantityInBaseUnits, decimal Total);
    public record CustomerPaymentMixDto(PaymentMethod PaymentMethod, int PurchaseCount, decimal Total);

    /// <param name="Summary">Null when the customer bought nothing in the period.</param>
    /// <param name="Reasons">Plain sentences behind the segment and the suggestion, for the person deciding.</param>
    /// <param name="TypicalDaysBetweenPurchases">The business's median among repeat customers in the period: what "regular" is measured against.</param>
    public record CustomerInsightsDto(Guid CustomerId, string CustomerName, string? PhoneNumber, DateOnly From, DateOnly To,
        CustomerBuyingSummaryDto? Summary, IEnumerable<CustomerMonthDto> ByMonth, IEnumerable<CustomerProductDto> TopProducts,
        IEnumerable<CustomerPaymentMixDto> ByPaymentMethod, int DeliveredByRider, int BoughtAtCounter, decimal ReturnedAmount,
        decimal? Balance, decimal? CreditLimit, decimal? DiscountPercent, string? DiscountNote, DateTime? DiscountSetAt,
        decimal? MaxDiscountPercent, decimal? TypicalDaysBetweenPurchases, IEnumerable<string> Reasons);

    /// <summary>from/to: local days, inclusive. Left out: the last 90 days.</summary>
    public record CustomerInsightsQuery(DateOnly? From = null, DateOnly? To = null, CustomerSegment? Segment = null,
        CustomerSuggestedAction? SuggestedAction = null, string? Search = null, CustomerInsightsSort Sort = CustomerInsightsSort.Spend,
        int UtcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes);

    /// <summary>The dashboard's customer panel: the last 90 days to the dashboard's end date.</summary>
    /// <param name="WalkInRevenue">Completed sales with no customer: buyers the station cannot reward or follow up.</param>
    public record DashboardCustomersDto(DateOnly From, DateOnly To, int ActiveCustomers, int NewCustomers, int RegularCustomers,
        int SlippingCustomers, int DiscountCandidates, int CustomersWithDiscount, decimal RegisteredRevenue, decimal WalkInRevenue,
        IEnumerable<CustomerBuyingSummaryDto> TopCustomers, IEnumerable<CustomerBuyingSummaryDto> Candidates,
        IEnumerable<CustomerBuyingSummaryDto> Slipping);

    /// <summary>A completed sale reduced to what the insights need. CustomerId null = walk-in.</summary>
    public record CustomerSaleRow(Guid SaleId, Guid? CustomerId, DateTime CreatedAt, decimal TotalAmount, decimal ReturnedAmount,
        decimal CustomerDiscountAmount, PaymentMethod PaymentMethod, DeliveryMode DeliveryMode);
    public record CustomerLineRow(Guid ProductId, string ProductName, string? BaseUnitSymbol, decimal Quantity, decimal QuantityReturned,
        decimal QuantityInBaseUnits, decimal LineTotal);

    public interface ICustomerInsightsRepository
    {
        /// <summary>Completed sales in the window, walk-ins included; one customer's only when given.</summary>
        Task<IReadOnlyList<CustomerSaleRow>> GetSalesAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? customerId);
        /// <summary>Each customer's first completed purchase ever.</summary>
        Task<Dictionary<Guid, DateTime>> GetFirstPurchasesAsync(IEnumerable<Guid> customerIds);
        Task<IReadOnlyList<CustomerLineRow>> GetLinesAsync(Guid customerId, DateTime fromUtc, DateTime toUtcExclusive);
        Task<IReadOnlyList<Customer>> GetCustomersAsync(IEnumerable<Guid> customerIds);
        Task<Customer?> SetDiscountAsync(Guid customerId, decimal? discountPercent, string? note, Guid? userId);
    }

    /// <summary>
    /// How customers have been buying, so the owner can decide who deserves a standing discount, and the discount itself
    /// (Customer.DiscountPercent), which every later sale to the customer applies on the server.
    /// </summary>
    public class CustomerInsightsService
    {
        public const int DefaultWindowDays = 90;
        public const int DetailWindowDays = 365;
        private const int NewCustomerDays = 30;
        private const decimal TopShare = 0.2m;
        private const int MinSlippingDays = 7;
        private const int DashboardListSize = 5;
        private const int TopProductCount = 5;
        private const int MaxNoteLength = 500;

        private readonly ICustomerInsightsRepository _repository;
        private readonly IBusinessRepository _businessRepository;
        private readonly IAccountingRepository _accountingRepository;
        private readonly ITenantContext _tenant;
        private readonly ICurrentUser _currentUser;
        private readonly ISystemEventRecorder _events;

        public CustomerInsightsService(ICustomerInsightsRepository repository, IBusinessRepository businessRepository,
            IAccountingRepository accountingRepository, ITenantContext tenant, ICurrentUser currentUser, ISystemEventRecorder events)
        {
            _repository = repository;
            _businessRepository = businessRepository;
            _accountingRepository = accountingRepository;
            _tenant = tenant;
            _currentUser = currentUser;
            _events = events;
        }

        // ---- The list ----

        public async Task<PagedResult<CustomerBuyingSummaryDto>> GetCustomersAsync(int page, int pageSize, CustomerInsightsQuery query)
        {
            _tenant.RequireBusinessId();
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var period = Window(query.From, query.To, query.UtcOffsetMinutes, DefaultWindowDays);
            var (summaries, _) = await SummariesAsync(period);

            IEnumerable<CustomerBuyingSummaryDto> rows = summaries;
            if (query.Segment is { } segment)
                rows = rows.Where(r => r.Segment == segment);
            if (query.SuggestedAction is { } action)
                rows = rows.Where(r => r.SuggestedAction == action);
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim();
                rows = rows.Where(r => r.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase)
                                       || (r.PhoneNumber ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
            }
            rows = query.Sort switch
            {
                CustomerInsightsSort.Purchases => rows.OrderByDescending(r => r.PurchaseCount).ThenBy(r => r.SpendRank),
                CustomerInsightsSort.Recent => rows.OrderByDescending(r => r.LastPurchaseAt),
                _ => rows.OrderBy(r => r.SpendRank)
            };
            var list = rows.ToList();
            return new PagedResult<CustomerBuyingSummaryDto>(list.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, pageSize, list.Count);
        }

        // ---- One customer ----

        /// <summary>One customer's buying in the period (default: the last 365 days), placed among everyone else's.</summary>
        public async Task<CustomerInsightsDto> GetCustomerAsync(Guid customerId, DateOnly? from, DateOnly? to, int utcOffsetMinutes)
        {
            var businessId = _tenant.RequireBusinessId();
            var customer = await _businessRepository.GetCustomerByIdAsync(customerId)
                ?? throw new NotFoundException($"Customer '{customerId}' not found.");
            var period = Window(from, to, utcOffsetMinutes, DetailWindowDays);
            var (summaries, typical) = await SummariesAsync(period);
            var summary = summaries.FirstOrDefault(s => s.CustomerId == customerId);

            var sales = await _repository.GetSalesAsync(period.FromUtc, period.ToUtcExclusive, customerId);
            var lines = await _repository.GetLinesAsync(customerId, period.FromUtc, period.ToUtcExclusive);
            var business = await _businessRepository.GetBusinessByIdAsync(businessId);
            var balance = await _accountingRepository.GetCustomerBalanceAsync(customerId);

            var byMonth = Months(period).Select(m =>
                {
                    var theirs = sales.Where(s => MonthOf(s.CreatedAt, period) == m).ToList();
                    return new CustomerMonthDto(m, theirs.Count, theirs.Sum(Net));
                }).ToList();
            var topProducts = lines.GroupBy(l => l.ProductId)
                .Select(g => new CustomerProductDto(g.Key, g.First().ProductName, g.First().BaseUnitSymbol,
                    g.Sum(l => l.Quantity == 0 ? 0 : Math.Round(l.QuantityInBaseUnits * (l.Quantity - l.QuantityReturned) / l.Quantity, 4)),
                    g.Sum(l => l.LineTotal)))
                .OrderByDescending(p => p.Total).Take(TopProductCount).ToList();
            var mix = sales.GroupBy(s => s.PaymentMethod)
                .Select(g => new CustomerPaymentMixDto(g.Key, g.Count(), g.Sum(Net))).OrderByDescending(m => m.Total).ToList();

            return new CustomerInsightsDto(customer.CustomerId, customer.CustomerName, customer.PhoneNumber, period.From, period.To, summary,
                byMonth, topProducts, mix, sales.Count(s => s.DeliveryMode == DeliveryMode.Rider), sales.Count(s => s.DeliveryMode == DeliveryMode.Counter),
                sales.Sum(s => s.ReturnedAmount), balance?.OutstandingBalance, customer.CreditLimit, customer.DiscountPercent, customer.DiscountNote,
                customer.DiscountSetAt, business?.MaxDiscountPercent, typical, Reasons(summary, typical, summaries.Count));
        }

        // ---- The dashboard panel ----

        public async Task<DashboardCustomersDto> GetDashboardAsync(DateOnly to, int utcOffsetMinutes)
        {
            _tenant.RequireBusinessId();
            var period = ReportPeriod.Create(to.AddDays(1 - DefaultWindowDays), to, utcOffsetMinutes);
            var (summaries, _) = await SummariesAsync(period);
            var walkIn = (await _repository.GetSalesAsync(period.FromUtc, period.ToUtcExclusive, null)).Where(s => s.CustomerId is null).Sum(Net);

            var candidates = summaries.Where(s => s.SuggestedAction == CustomerSuggestedAction.ConsiderDiscount).ToList();
            var slipping = summaries.Where(s => s.Segment == CustomerSegment.Slipping).ToList();
            return new DashboardCustomersDto(period.From, period.To, summaries.Count,
                summaries.Count(s => s.Segment == CustomerSegment.New), summaries.Count(s => s.Segment is CustomerSegment.Regular or CustomerSegment.Top),
                slipping.Count, candidates.Count, summaries.Count(s => s.DiscountPercent is > 0), summaries.Sum(s => s.TotalSpent), walkIn,
                summaries.Take(DashboardListSize).ToList(), candidates.Take(DashboardListSize).ToList(), slipping.Take(DashboardListSize).ToList());
        }

        // ---- The discount ----

        /// <summary>
        /// Gives (or removes) the customer's standing discount. Every sale to them from now on is discounted by it, on the server; sales
        /// already made keep what they were charged. Capped by the business's MaxDiscountPercent.
        /// </summary>
        public async Task<CustomerDto> SetDiscountAsync(Guid customerId, SetCustomerDiscountRequestDto request)
        {
            var businessId = _tenant.RequireBusinessId();
            var customer = await _businessRepository.GetCustomerByIdAsync(customerId)
                ?? throw new NotFoundException($"Customer '{customerId}' not found.");
            var percent = request.DiscountPercent is > 0 ? request.DiscountPercent : null;
            var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            if (percent is { } p)
            {
                if (p >= 100 || decimal.Round(p, 2) != p)
                    throw new ValidationException("discountPercent", "A discount is a percentage below 100, with at most 2 decimals.");
                var business = await _businessRepository.GetBusinessByIdAsync(businessId);
                if (business?.MaxDiscountPercent is { } cap && p > cap)
                    throw new ValidationException("discountPercent", $"Discounts are capped at {cap:0.##}% for this business.");
                if (note is null)
                    throw new ValidationException("note", "Why is the customer getting it? A note is required (e.g. buys 20L three times a week).");
            }
            else if (request.DiscountPercent is < 0)
                throw new ValidationException("discountPercent", "A discount cannot be negative.");
            if (note is { Length: > MaxNoteLength })
                throw new ValidationException("note", $"The note is at most {MaxNoteLength} characters.");

            _events.Record(new SystemEventDraft(SystemEventTypes.CustomerDiscountChanged,
                percent is { } given
                    ? $"{customer.CustomerName} now gets {given:0.##}% off every sale" + (customer.DiscountPercent is { } was ? $" (was {was:0.##}%)" : "") + $": {note}"
                    : $"{customer.CustomerName}'s standing discount" + (customer.DiscountPercent is { } old ? $" of {old:0.##}%" : "") + " was removed.",
                EntityType: "Customer", EntityId: customerId,
                Data: new { From = customer.DiscountPercent, To = percent, note }));
            var updated = await _repository.SetDiscountAsync(customerId, percent, percent is null ? null : note, _currentUser.UserId)
                ?? throw new NotFoundException($"Customer '{customerId}' not found.");
            return new CustomerDto(updated.CustomerId, updated.BusinessId, updated.CustomerName, updated.KraPin, updated.ContactPerson,
                updated.PhoneNumber, updated.Email, updated.PhysicalAddress, updated.IsActive, updated.PriceType, updated.CreditLimit,
                updated.DiscountPercent, updated.DiscountNote, updated.DiscountSetAt);
        }

        // ---- Working it out ----

        private async Task<(List<CustomerBuyingSummaryDto> Summaries, decimal? Typical)> SummariesAsync(ReportPeriod period)
        {
            var sales = (await _repository.GetSalesAsync(period.FromUtc, period.ToUtcExclusive, null)).Where(s => s.CustomerId is not null).ToList();
            var ids = sales.Select(s => s.CustomerId!.Value).Distinct().ToList();
            var customers = (await _repository.GetCustomersAsync(ids)).ToDictionary(c => c.CustomerId);
            var firsts = await _repository.GetFirstPurchasesAsync(ids);
            var stats = sales.GroupBy(s => s.CustomerId!.Value)
                .Where(g => customers.ContainsKey(g.Key))
                .Select(g => new BuyingStats(g.Key, g.Select(s => s.CreatedAt).ToList(), g.Sum(Net), g.Sum(s => s.CustomerDiscountAmount),
                    firsts.TryGetValue(g.Key, out var first) ? first : null))
                .ToList();
            var asOf = DateTime.UtcNow < period.ToUtcExclusive ? DateTime.UtcNow : period.ToUtcExclusive;
            var classified = Classify(stats, asOf);
            var summaries = classified.Results.Select(r =>
                {
                    var c = customers[r.Stats.CustomerId];
                    var action = SuggestFor(r.Segment, c.DiscountPercent);
                    return new CustomerBuyingSummaryDto(c.CustomerId, c.CustomerName, c.PhoneNumber, r.Stats.Count, r.Stats.Spent,
                        RoundMoney(r.Stats.Spent / r.Stats.Count), r.Stats.FirstEver, r.Stats.Last, r.DaysSince, r.Stats.AverageInterval,
                        c.DiscountPercent, r.Stats.DiscountGiven, r.Segment, action, r.Rank);
                })
                .OrderBy(s => s.SpendRank).ToList();
            return (summaries, classified.TypicalInterval);
        }

        /// <summary>One customer's purchases in the window.</summary>
        public sealed record BuyingStats(Guid CustomerId, IReadOnlyList<DateTime> Purchases, decimal Spent, decimal DiscountGiven, DateTime? FirstEver)
        {
            public int Count => Purchases.Count;
            public DateTime Last => Purchases.Max();
            /// <summary>Days from the first to the last purchase in the window, over the gaps between them. Null with one purchase.</summary>
            public decimal? AverageInterval => Count < 2 ? null
                : Math.Round((decimal)(Purchases.Max() - Purchases.Min()).TotalDays / (Count - 1), 1);
        }

        public sealed record Classified(BuyingStats Stats, CustomerSegment Segment, int Rank, int DaysSince);

        /// <summary>Segments, relative to the other customers in the same window (pure: unit-tested directly).</summary>
        public static (IReadOnlyList<Classified> Results, decimal? TypicalInterval) Classify(IReadOnlyList<BuyingStats> stats, DateTime asOfUtc)
        {
            var ranked = stats.OrderByDescending(s => s.Spent).ThenByDescending(s => s.Count).ThenBy(s => s.CustomerId).ToList();
            var topCount = ranked.Count == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(ranked.Count * TopShare));
            var intervals = ranked.Where(s => s.AverageInterval is not null).Select(s => s.AverageInterval!.Value).OrderBy(v => v).ToList();
            decimal? typical = intervals.Count == 0 ? null
                : intervals.Count % 2 == 1 ? intervals[intervals.Count / 2]
                : Math.Round((intervals[intervals.Count / 2 - 1] + intervals[intervals.Count / 2]) / 2, 1);

            var results = ranked.Select((s, i) =>
                {
                    var daysSince = Math.Max(0, (int)(asOfUtc - s.Last).TotalDays);
                    var slipping = s.AverageInterval is { } own && daysSince >= MinSlippingDays && daysSince > 2 * own;
                    var segment = slipping ? CustomerSegment.Slipping
                        : i < topCount && s.Count >= 2 ? CustomerSegment.Top
                        : s.AverageInterval is { } interval && typical is { } t && interval <= t ? CustomerSegment.Regular
                        : s.FirstEver is { } first && (asOfUtc - first).TotalDays <= NewCustomerDays ? CustomerSegment.New
                        : CustomerSegment.Occasional;
                    return new Classified(s, segment, i + 1, daysSince);
                })
                .ToList();
            return (results, typical);
        }

        public static CustomerSuggestedAction SuggestFor(CustomerSegment segment, decimal? discountPercent)
        {
            var hasDiscount = discountPercent is > 0;
            return segment switch
            {
                CustomerSegment.Top or CustomerSegment.Regular when !hasDiscount => CustomerSuggestedAction.ConsiderDiscount,
                CustomerSegment.Slipping when !hasDiscount => CustomerSuggestedAction.WinBack,
                CustomerSegment.Slipping or CustomerSegment.Occasional when hasDiscount => CustomerSuggestedAction.ReviewDiscount,
                _ => CustomerSuggestedAction.None
            };
        }

        private static IReadOnlyList<string> Reasons(CustomerBuyingSummaryDto? s, decimal? typical, int customerCount)
        {
            if (s is null)
                return new[] { "No completed purchases in this period." };
            var reasons = new List<string>
            {
                $"{s.PurchaseCount} purchase(s), {s.TotalSpent:0.00} in total: number {s.SpendRank} of {customerCount} customers by spend.",
                s.AverageDaysBetweenPurchases is { } every
                    ? $"Buys about every {every:0.#} day(s)" + (typical is { } t ? $"; a typical repeat customer buys every {t:0.#} day(s)." : ".")
                    : "Bought only once in this period.",
                $"Last bought {s.DaysSinceLastPurchase} day(s) ago."
            };
            reasons.Add(s.SuggestedAction switch
            {
                CustomerSuggestedAction.ConsiderDiscount => "A loyal, valuable customer with no standing discount: a discount could keep them.",
                CustomerSuggestedAction.WinBack => "Has stayed away much longer than usual for them: worth a call, perhaps an offer.",
                CustomerSuggestedAction.ReviewDiscount => $"Has a {s.DiscountPercent:0.##}% discount but is not buying regularly: review it.",
                _ => "No action suggested."
            });
            return reasons;
        }

        private static decimal Net(CustomerSaleRow s) => s.TotalAmount - s.ReturnedAmount;

        private static ReportPeriod Window(DateOnly? from, DateOnly? to, int utcOffsetMinutes, int days)
        {
            ReportPeriod.ValidateOffset(utcOffsetMinutes);
            var end = to ?? ReportPeriod.TodayAt(utcOffsetMinutes);
            var start = from ?? end.AddDays(1 - days);
            if (start > end)
                throw new ValidationException("from", "'from' cannot be after 'to'.");
            return ReportPeriod.Create(start, end, utcOffsetMinutes);
        }

        private static string MonthOf(DateTime utc, ReportPeriod period) => period.LocalDateOf(utc).ToString("yyyy-MM");

        private static IEnumerable<string> Months(ReportPeriod period)
        {
            for (var m = new DateOnly(period.From.Year, period.From.Month, 1); m <= period.To; m = m.AddMonths(1))
                yield return m.ToString("yyyy-MM");
        }

        private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
