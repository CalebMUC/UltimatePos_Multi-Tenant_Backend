using System;

namespace UltimatePos.Application.Reports
{
    /// <summary>Shared caps on how many rows a report may list, so no endpoint can be asked for an unbounded response.</summary>
    public static class ReportLimits
    {
        public const int DefaultTop = 10;
        public const int MaxTop = 50;

        public const int DefaultRows = 100;
        public const int MaxRows = 500;

        /// <summary>For "top N" lists: 0 or negative means the default, anything above the maximum is capped.</summary>
        public static int ClampTop(int top) => top <= 0 ? DefaultTop : Math.Min(top, MaxTop);

        /// <summary>For detail listings (individual adjustments, price points): same rule with larger limits.</summary>
        public static int ClampRows(int rows) => rows <= 0 ? DefaultRows : Math.Min(rows, MaxRows);
    }
}
