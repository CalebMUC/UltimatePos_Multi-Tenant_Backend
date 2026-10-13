using System;
using System.Collections.Generic;
using System.Linq;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Application.Costing
{
    /// <summary>
    /// The costing arithmetic, in one pure place. Unit costs are per BASE unit at 6dp (a 25kg bag at KES 3,125 is
    /// 125/kg; a gram-priced premix needs the extra precision); money totals are rounded to 2dp.
    /// The moving-average update itself is atomic SQL inside StockLedger, not C#, so it cannot lose concurrent updates.
    /// </summary>
    public static class CostingCalculator
    {
        public const int UnitCostDecimals = 6;
        public const int MoneyDecimals = 2;

        /// <summary>Converts a cost quoted per pack unit (a 25kg bag) into a cost per base unit (kg).</summary>
        public static decimal ToUnitCostPerBaseUnit(decimal costPerPackUnit, decimal conversionFactor)
        {
            if (conversionFactor <= 0)
                throw new InvalidAssignmentException("Unit conversion factor must be greater than zero.");
            return Math.Round(costPerPackUnit / conversionFactor, UnitCostDecimals, MidpointRounding.AwayFromZero);
        }

        public static decimal LineCost(decimal quantityInBaseUnits, decimal unitCostPerBaseUnit) =>
            Math.Round(quantityInBaseUnits * unitCostPerBaseUnit, MoneyDecimals, MidpointRounding.AwayFromZero);

        public static decimal RoundMoney(decimal value) =>
            Math.Round(value, MoneyDecimals, MidpointRounding.AwayFromZero);

        public sealed record ProductionCost(decimal MaterialCost, decimal AdditionalCost, decimal TotalCost, decimal UnitCostPerBaseUnit);

        /// <summary>
        /// Rolls a production run's costs up. Material = the cost of every consumed stock item (ingredients and tracked
        /// packaging, each already rounded); additional = labour, utilities and other non-stock costs. The finished
        /// good's unit cost is total / ACTUAL output, so a poor yield makes each unit more expensive, as it should.
        /// </summary>
        public static ProductionCost ForProduction(
            IEnumerable<decimal> materialLineCosts, IEnumerable<decimal> additionalCosts, decimal outputQuantityInBaseUnits)
        {
            if (outputQuantityInBaseUnits <= 0)
                throw new InvalidAssignmentException("Output quantity must be greater than zero to cost a production run.");

            var material = materialLineCosts.Sum();
            var additional = additionalCosts.Sum();
            var total = material + additional;
            var unit = Math.Round(total / outputQuantityInBaseUnits, UnitCostDecimals, MidpointRounding.AwayFromZero);
            return new ProductionCost(material, additional, total, unit);
        }

        /// <summary>Gross margin on price: (price - cost) / price. Percent is null when the price is zero.</summary>
        public static (decimal Margin, decimal? MarginPercent) Margin(decimal price, decimal cost)
        {
            var margin = RoundMoney(price - cost);
            decimal? percent = price == 0 ? null : Math.Round((price - cost) / price * 100m, 2, MidpointRounding.AwayFromZero);
            return (margin, percent);
        }
    }
}