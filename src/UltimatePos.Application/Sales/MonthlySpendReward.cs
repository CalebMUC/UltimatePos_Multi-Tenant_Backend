using UltimatePos.Application.Reports;
using UltimatePos.Application.Sales.Dtos;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Sales
{
    /// <summary>
    /// The monthly-spend reward (BusinessProfile.MonthlyDiscountThreshold / Percent / Scope). A calendar month in East Africa Time; what
    /// counts is the customer's completed purchases at every branch, net of returns, before the sale being priced. Reaching the threshold
    /// is what qualifies, so the purchase that reaches it pays full price and the next one is discounted. A new month starts from zero.
    /// </summary>
    public static class MonthlySpendReward
    {
        /// <summary>The customer's month so far; null when the business has no reward.</summary>
        public static async Task<CustomerMonthlySpendDto?> ForCustomerAsync(ISalesRepository repository, BusinessProfile business, Guid customerId,
            DateOnly today)
        {
            if (business.MonthlyDiscountThreshold is not { } threshold || business.MonthlyDiscountPercent is not { } percent || percent <= 0)
                return null;
            var month = new DateOnly(today.Year, today.Month, 1);
            var period = ReportPeriod.Create(month, month.AddMonths(1).AddDays(-1), ReportPeriod.DefaultUtcOffsetMinutes);
            var (spent, used) = await repository.GetCustomerSpendAsync(customerId, period.FromUtc, period.ToUtcExclusive);
            return Evaluate(month, spent, used, threshold, percent, business.MonthlyDiscountScope);
        }

        /// <param name="used">Purchases this month that already had the reward.</param>
        public static CustomerMonthlySpendDto Evaluate(DateOnly month, decimal spent, int used, decimal threshold, decimal percent,
            MonthlyDiscountScope scope) =>
            new(month.ToString("yyyy-MM"), spent, threshold, percent, scope, Math.Max(0m, threshold - spent),
                spent >= threshold && (scope == MonthlyDiscountScope.RestOfMonth || used == 0));
    }
}
