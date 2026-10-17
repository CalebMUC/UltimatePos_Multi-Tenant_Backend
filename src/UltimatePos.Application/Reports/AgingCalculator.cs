using System;
using System.Collections.Generic;
using System.Linq;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Reports
{
    /// <summary>
    /// Customer ageing, in one pure place. The ledger only stores invoices and payments, not which payment settled which
    /// invoice, so this works it out the usual open-item way:
    ///   1. A reversal (a voided sale) cancels the invoice it points at, in full.
    ///   2. Everything else the customer paid (and any reversal that matches no invoice) forms a pool of credit.
    ///   3. That credit settles the OLDEST invoices first.
    ///   4. Whatever is still open is aged from its invoice date; credit left over is reported as unapplied.
    /// Invariant: Outstanding - UnappliedCredit always equals the customer's ledger balance (invoices minus payments minus reversals).
    /// </summary>
    public static class AgingCalculator
    {
        public const int FirstBucketDays = 30;
        public const int SecondBucketDays = 60;
        public const int ThirdBucketDays = 90;

        public const string Bucket0To30 = "0-30";
        public const string Bucket31To60 = "31-60";
        public const string Bucket61To90 = "61-90";
        public const string BucketOver90 = "90+";

        public sealed record OpenInvoice(Guid InvoiceId, DateOnly InvoiceDate, decimal OriginalAmount, decimal OutstandingAmount, int AgeDays, string Bucket);

        public sealed record CustomerAging(decimal Outstanding, decimal UnappliedCredit, decimal Days0To30, decimal Days31To60,
            decimal Days61To90, decimal Over90, IReadOnlyList<OpenInvoice> OpenInvoices);

        /// <param name="entries">One customer's ledger entries, already limited to those on or before the as-of day.</param>
        /// <param name="asOf">The local calendar day invoices are aged to.</param>
        /// <param name="offset">The business's offset from UTC, used to turn invoice timestamps into local dates.</param>
        public static CustomerAging Calculate(IEnumerable<CustomerLedgerRow> entries, DateOnly asOf, TimeSpan offset)
        {
            var ordered = entries.OrderBy(e => e.CreatedAt).ToList();

            var invoices = ordered
                .Where(e => e.EntryType == CustomerLedgerEntryType.Invoice)
                .Select(e => new Working(e.ReferenceId, e.CreatedAt, e.Amount))
                .ToList();

            var credit = 0m;
            foreach (var entry in ordered.Where(e => e.EntryType != CustomerLedgerEntryType.Invoice))
            {
                var target = entry.EntryType == CustomerLedgerEntryType.Reversal
                    ? invoices.FirstOrDefault(i => i.InvoiceId == entry.ReferenceId && i.Cancelled < i.Original)
                    : null;

                if (target is null)
                {
                    credit += entry.Amount; // a payment, or a reversal with no matching invoice
                    continue;
                }

                var cancelled = Math.Min(entry.Amount, target.Original - target.Cancelled);
                target.Cancelled += cancelled;
                credit += entry.Amount - cancelled; // a reversal bigger than its invoice: the rest is credit
            }

            foreach (var invoice in invoices.OrderBy(i => i.CreatedAt))
            {
                var payable = invoice.Original - invoice.Cancelled;
                var applied = Math.Min(credit, payable);
                invoice.Outstanding = payable - applied;
                credit -= applied;
            }

            var open = invoices
                .Where(i => i.Outstanding > 0)
                .OrderBy(i => i.CreatedAt)
                .Select(i =>
                {
                    var date = ReportPeriod.ToLocalDate(i.CreatedAt, offset);
                    var age = Math.Max(0, asOf.DayNumber - date.DayNumber);
                    return new OpenInvoice(i.InvoiceId, date, i.Original, i.Outstanding, age, BucketFor(age));
                })
                .ToList();

            return new CustomerAging(
                open.Sum(i => i.OutstandingAmount),
                credit,
                SumBucket(open, Bucket0To30), SumBucket(open, Bucket31To60), SumBucket(open, Bucket61To90), SumBucket(open, BucketOver90),
                open);
        }

        public static string BucketFor(int ageDays) =>
            ageDays <= FirstBucketDays ? Bucket0To30
            : ageDays <= SecondBucketDays ? Bucket31To60
            : ageDays <= ThirdBucketDays ? Bucket61To90
            : BucketOver90;

        private static decimal SumBucket(IEnumerable<OpenInvoice> invoices, string bucket) =>
            invoices.Where(i => i.Bucket == bucket).Sum(i => i.OutstandingAmount);

        private sealed class Working
        {
            public Working(Guid invoiceId, DateTime createdAt, decimal original)
            {
                InvoiceId = invoiceId;
                CreatedAt = createdAt;
                Original = original;
            }

            public Guid InvoiceId { get; }
            public DateTime CreatedAt { get; }
            public decimal Original { get; }
            public decimal Cancelled { get; set; }
            public decimal Outstanding { get; set; }
        }
    }
}
