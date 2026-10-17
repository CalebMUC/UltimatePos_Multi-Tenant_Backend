using System;
using UltimatePos.Application.Costing;

namespace UltimatePos.Application.Reports
{
    /// <summary>Small helpers every report uses, so they all round and guard against dividing by zero the same way.</summary>
    public static class ReportMath
    {
        /// <summary>total / count rounded as money; 0 when there is nothing to average.</summary>
        public static decimal Average(decimal total, int count) =>
            count == 0 ? 0m : CostingCalculator.RoundMoney(total / count);

        /// <summary>totalCost / quantity rounded as money; 0 when the quantity is 0.</summary>
        public static decimal CostPerUnit(decimal totalCost, decimal quantity) =>
            quantity == 0 ? 0m : CostingCalculator.RoundMoney(totalCost / quantity);

        /// <summary>part / whole as a percentage to 2 decimal places; 0 when there is no whole to measure against.</summary>
        public static decimal Percent(decimal part, decimal whole) =>
            whole == 0 ? 0m : Math.Round(part / whole * 100m, 2, MidpointRounding.AwayFromZero);
    }
}
