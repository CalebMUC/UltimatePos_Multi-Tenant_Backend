using UltimatePos.Application.Business.Dtos;

namespace UltimatePos.Application.Business
{
    /// <summary>
    /// When a business may sell age-restricted products (alcohol, tobacco), from the hours its liquor licence gives: one window on weekdays
    /// and one at weekends. A window whose end is before its start runs past midnight, so 17:00-02:00 on a Friday still covers 01:30 on
    /// Saturday morning. No hours set = any time.
    /// </summary>
    public static class SellingHours
    {
        /// <summary>True when <paramref name="localNow"/> (East Africa Time) falls inside today's window, or inside yesterday's when it ran past midnight.</summary>
        public static bool IsOpen(AgeRestrictedSalesSettingsDto settings, DateTime localNow)
        {
            if (settings.WeekdayFrom is null || settings.WeekdayTo is null)
                return true;

            var time = TimeOnly.FromDateTime(localNow);
            var (from, to) = WindowOn(settings, localNow.DayOfWeek);
            if (from < to ? time >= from && time < to : time >= from)
                return true;

            var (yesterdayFrom, yesterdayTo) = WindowOn(settings, localNow.AddDays(-1).DayOfWeek);
            return yesterdayFrom > yesterdayTo && time < yesterdayTo;
        }

        /// <summary>The hours as a till shows them when it refuses a sale: "17:00-23:00 on weekdays, 14:00-23:00 at weekends".</summary>
        public static string Describe(AgeRestrictedSalesSettingsDto settings)
        {
            if (settings.WeekdayFrom is not { } from || settings.WeekdayTo is not { } to)
                return "at any time";
            var weekday = $"{from:HH\\:mm}-{to:HH\\:mm}";
            if (settings.WeekendFrom is not { } weekendFrom || settings.WeekendTo is not { } weekendTo
                || (weekendFrom == from && weekendTo == to))
                return $"{weekday} every day";
            return $"{weekday} on weekdays, {weekendFrom:HH\\:mm}-{weekendTo:HH\\:mm} at weekends";
        }

        private static (TimeOnly From, TimeOnly To) WindowOn(AgeRestrictedSalesSettingsDto s, DayOfWeek day) =>
            day is DayOfWeek.Saturday or DayOfWeek.Sunday && s.WeekendFrom is { } from && s.WeekendTo is { } to
                ? (from, to)
                : (s.WeekdayFrom!.Value, s.WeekdayTo!.Value);
    }
}
