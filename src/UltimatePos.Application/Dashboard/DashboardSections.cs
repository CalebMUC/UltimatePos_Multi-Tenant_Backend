using System;
using System.Collections.Generic;
using System.Linq;

namespace UltimatePos.Application.Dashboard
{
    /// <summary>
    /// The parts of the dashboard a user can be shown. Each part is backed by the permission that already protects the same data
    /// elsewhere in the API, so the dashboard can never show someone a number they could not get from the screen it came from.
    /// </summary>
    [Flags]
    public enum DashboardSections
    {
        None = 0,
        Sales = 1,
        Profit = 2,
        Receivables = 4,
        Stock = 8,
        /// <summary>Not a section of its own: the stock VALUE inside the Stock section, which is cost data.</summary>
        StockValue = 16,
        Production = 32,
        /// <summary>How customers have been buying: top customers, regulars, those slipping away, and discount candidates.</summary>
        Customers = 64,
    }

    public static class DashboardPermissions
    {
        /// <summary>Needed for every dashboard endpoint. On its own it shows nothing: each section also needs its own permission.</summary>
        public const string View = "Dashboard.View";

        /// <summary>Section to the permission that unlocks it. Keep in step with the controller attributes and the DbSeeder.</summary>
        public static readonly IReadOnlyList<(DashboardSections Section, string Permission)> Required = new[]
        {
            (DashboardSections.Sales, "Sales.View"),
            (DashboardSections.Profit, "Costing.View"),
            (DashboardSections.Receivables, "Customers.ViewLedger"),
            (DashboardSections.Stock, "Stock.View"),
            (DashboardSections.StockValue, "Costing.View"),
            (DashboardSections.Production, "Production.View"),
            (DashboardSections.Customers, "Customers.ViewInsights"),
        };

        public static string PermissionFor(DashboardSections section) =>
            Required.First(r => r.Section == section).Permission;

        /// <summary>Every permission the dashboard relies on, including Dashboard.View.</summary>
        public static IEnumerable<string> All => Required.Select(r => r.Permission).Append(View).Distinct();

        private static readonly DashboardSections[] Visible =
            { DashboardSections.Sales, DashboardSections.Profit, DashboardSections.Receivables, DashboardSections.Stock, DashboardSections.Production,
              DashboardSections.Customers };

        /// <summary>Names of the sections in <paramref name="allowed"/> (StockValue is a detail of Stock, so it is not listed).</summary>
        public static IReadOnlyList<string> NamesIn(DashboardSections allowed) =>
            Visible.Where(s => allowed.HasFlag(s)).Select(s => s.ToString()).ToList();

        /// <summary>Names of the sections NOT in <paramref name="allowed"/>, so a client knows which panels to leave out.</summary>
        public static IReadOnlyList<string> NamesNotIn(DashboardSections allowed) =>
            Visible.Where(s => !allowed.HasFlag(s)).Select(s => s.ToString()).ToList();

        public static bool HasAnySection(DashboardSections allowed) =>
            Visible.Any(s => allowed.HasFlag(s));
    }
}
