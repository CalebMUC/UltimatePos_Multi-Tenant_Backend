using System;
using UltimatePos.Application.Expenses;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Application.Reports
{
    /// <summary>
    /// A window of whole days in the business's local time, resolved to exact UTC instants. Same convention as the
    /// profit-and-loss report: `from` and `to` are calendar dates (both inclusive) and utcOffsetMinutes (default 180,
    /// East Africa Time) decides which sales fall on which day. Expenses carry a plain date and need no offset.
    /// </summary>
    public sealed class ReportPeriod
    {
        public const int DefaultUtcOffsetMinutes = 180;
        private const int MaxOffsetMinutes = 14 * 60;

        public DateOnly From { get; }
        public DateOnly To { get; }
        public TimeSpan Offset { get; }

        private ReportPeriod(DateOnly from, DateOnly to, TimeSpan offset)
        {
            From = from;
            To = to;
            Offset = offset;
        }

        public static ReportPeriod Create(DateOnly from, DateOnly to, int utcOffsetMinutes)
        {
            ValidateOffset(utcOffsetMinutes);
            ExpenseService.ValidateDateRange(from, to); // from <= to, years 2000..2200, at most 366 days
            return new ReportPeriod(from, to, TimeSpan.FromMinutes(utcOffsetMinutes));
        }

        /// <summary>Today's date for the business, given its offset from UTC.</summary>
        public static DateOnly TodayAt(int utcOffsetMinutes)
        {
            ValidateOffset(utcOffsetMinutes);
            return DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(utcOffsetMinutes));
        }

        public static void ValidateOffset(int utcOffsetMinutes)
        {
            if (Math.Abs(utcOffsetMinutes) > MaxOffsetMinutes)
                throw new InvalidAssignmentException("utcOffsetMinutes must be between -840 and 840.");
        }

        /// <summary>The UTC instant at which a local calendar day begins.</summary>
        public static DateTime StartOfLocalDayUtc(DateOnly day, TimeSpan offset) =>
            new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), offset).UtcDateTime;

        /// <summary>The local calendar day a UTC timestamp falls on.</summary>
        public static DateOnly ToLocalDate(DateTime utc, TimeSpan offset) =>
            DateOnly.FromDateTime(utc.Add(offset));

        /// <summary>Start of the first day, inclusive.</summary>
        public DateTime FromUtc => StartOfLocalDayUtc(From, Offset);

        /// <summary>Start of the day after the last day — an exclusive upper bound.</summary>
        public DateTime ToUtcExclusive => StartOfLocalDayUtc(To.AddDays(1), Offset);

        public int DayCount => To.DayNumber - From.DayNumber + 1;

        public DateOnly LocalDateOf(DateTime utc) => ToLocalDate(utc, Offset);
    }
}
