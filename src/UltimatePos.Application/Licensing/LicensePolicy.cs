using UltimatePos.Application.Reports;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Licensing
{
    /// <summary>Configuration section "Licensing".</summary>
    public sealed class LicensingSettings
    {
        /// <summary>Refuse access to businesses without a current licence. Off = licences are recorded and shown, never enforced.</summary>
        public bool Enforce { get; set; } = true;
        /// <summary>Days after a licence ends during which the business still works, with a warning.</summary>
        public int GraceDays { get; set; } = 7;
        /// <summary>Length of the trial a newly registered business starts with. 0 = no trial (it starts unlicensed).</summary>
        public int TrialDays { get; set; } = 14;
        /// <summary>Warn (and notify) this many days before a licence ends.</summary>
        public int ExpiryWarningDays { get; set; } = 14;
    }

    /// <summary>Where a business stands. Allowed decides whether its users may work; Warning is shown to them while still allowed.</summary>
    /// <param name="Status">Active, ExpiringSoon, GracePeriod, Expired, Unlicensed, Suspended or Closed.</param>
    /// <param name="Code">The error code when not allowed: BUSINESS_CLOSED, BUSINESS_SUSPENDED, LICENSE_EXPIRED or UNLICENSED.</param>
    public sealed record BusinessAccess(bool Allowed, string Status, string? Code, string? Message, DateOnly? LicenseEndsOn, int? DaysRemaining)
    {
        public string? Warning => Allowed && Status is "ExpiringSoon" or "GracePeriod" ? Message : null;
    }

    /// <summary>The one place that decides whether a business may be used today. Pure: everything it needs is passed in.</summary>
    public static class LicensePolicy
    {
        public static BusinessAccess Evaluate(BusinessProfile business, LicensingSettings settings, DateOnly today)
        {
            if (!business.IsActive)
                return new(false, "Closed", "BUSINESS_CLOSED", $"{business.BusinessName} has been closed. Contact the Ultimate POS team.", business.LicenseEndsOn, null);

            if (business.IsSuspended)
                return new(false, "Suspended", "BUSINESS_SUSPENDED",
                    $"{business.BusinessName} is suspended{(string.IsNullOrWhiteSpace(business.SuspensionReason) ? "" : $": {business.SuspensionReason}")}. Contact the Ultimate POS team.",
                    business.LicenseEndsOn, null);

            if (business.LicenseEndsOn is not { } endsOn)
                return new(!settings.Enforce, "Unlicensed", settings.Enforce ? "UNLICENSED" : null,
                    $"{business.BusinessName} has no licence. Contact the Ultimate POS team to activate it.", null, null);

            var daysLeft = endsOn.DayNumber - today.DayNumber;
            if (daysLeft >= 0)
                return daysLeft <= settings.ExpiryWarningDays
                    ? new(true, "ExpiringSoon", null, $"The licence ends on {endsOn:yyyy-MM-dd} ({Days(daysLeft)} left). Renew to avoid interruption.", endsOn, daysLeft)
                    : new(true, "Active", null, null, endsOn, daysLeft);

            var graceLeft = daysLeft + settings.GraceDays;
            if (graceLeft >= 0)
                return new(true, "GracePeriod", null,
                    $"The licence ended on {endsOn:yyyy-MM-dd}. Access stops in {Days(graceLeft)} unless it is renewed.", endsOn, daysLeft);

            return new(!settings.Enforce, "Expired", settings.Enforce ? "LICENSE_EXPIRED" : null,
                $"The licence for {business.BusinessName} ended on {endsOn:yyyy-MM-dd}. Renew it to continue.", endsOn, daysLeft);
        }

        public static DateOnly Today => ReportPeriod.TodayAt(ReportPeriod.DefaultUtcOffsetMinutes);

        /// <summary>
        /// What the business's licences add up to today: the last day of the unbroken run of periods covering today (renewals start
        /// the day after the previous one ends), and that licence's user limit. With nothing covering today, the most recent period
        /// that has ended — so its grace period applies. A licence that only starts later does not cover today. The user and branch
        /// limits are those of the licence in force today.
        /// </summary>
        public static (DateOnly? EndsOn, int? MaxUsers, int? MaxBranches) Coverage(IEnumerable<BusinessLicense> licenses, DateOnly today)
        {
            var active = licenses.Where(l => l.State == LicenseState.Active).OrderBy(l => l.StartsOn).ThenBy(l => l.EndsOn).ToList();
            var current = active.FirstOrDefault(l => l.StartsOn <= today && l.EndsOn >= today);
            if (current is null)
            {
                var lapsed = active.Where(l => l.EndsOn < today).MaxBy(l => l.EndsOn);
                return (lapsed?.EndsOn, lapsed?.MaxUsers, lapsed?.MaxBranches);
            }

            var endsOn = current.EndsOn;
            for (var next = Next(endsOn); next is not null; next = Next(endsOn))
                endsOn = next.EndsOn;
            return (endsOn, current.MaxUsers, current.MaxBranches);

            BusinessLicense? Next(DateOnly end) => active.FirstOrDefault(l => l.StartsOn <= end.AddDays(1) && l.EndsOn > end);
        }

        private static string Days(int n) => n == 0 ? "today" : n == 1 ? "1 day" : $"{n} days";
    }

    /// <summary>Cached access decisions, consulted on every request made in a business. Invalidate after changing a business's status.</summary>
    public interface IBusinessAccessGate
    {
        Task<BusinessAccess> CheckAsync(Guid businessId);
        void Invalidate(Guid businessId);
    }
}
