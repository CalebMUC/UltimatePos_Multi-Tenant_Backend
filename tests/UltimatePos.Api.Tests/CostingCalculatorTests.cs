using UltimatePos.Application.Costing;
using UltimatePos.Domain.Exceptions;

namespace Ultimate.Api.Tests;

public class CostingCalculatorTests
{
    [Theory]
    [InlineData(3125, 25, 125)]            // KES 3,125 per 25kg bag -> 125 per kg
    [InlineData(100, 3, 33.333333)]        // 6dp, rounded
    [InlineData(1, 1, 1)]
    public void Pack_price_converts_to_cost_per_base_unit(decimal packCost, decimal factor, decimal expected)
    {
        Assert.Equal(expected, CostingCalculator.ToUnitCostPerBaseUnit(packCost, factor));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_conversion_factor_is_rejected(decimal factor)
    {
        Assert.Throws<InvalidAssignmentException>(() => CostingCalculator.ToUnitCostPerBaseUnit(10m, factor));
    }

    [Fact]
    public void Line_cost_is_rounded_to_two_decimals()
    {
        Assert.Equal(170.00m, CostingCalculator.LineCost(12.5m, 13.6m));
        Assert.Equal(0.33m, CostingCalculator.LineCost(1m, 0.333333m));
    }

    [Fact]
    public void Production_cost_rolls_up_material_and_additional_costs_over_actual_output()
    {
        var cost = CostingCalculator.ForProduction(new[] { 3000m, 2000m }, new[] { 400m, 100m }, outputQuantityInBaseUnits: 200m);

        Assert.Equal(5000m, cost.MaterialCost);
        Assert.Equal(500m, cost.AdditionalCost);
        Assert.Equal(5500m, cost.TotalCost);
        Assert.Equal(27.5m, cost.UnitCostPerBaseUnit);
    }

    [Fact]
    public void A_poorer_yield_makes_each_unit_more_expensive()
    {
        var full = CostingCalculator.ForProduction(new[] { 5000m }, Array.Empty<decimal>(), 200m);
        var poor = CostingCalculator.ForProduction(new[] { 5000m }, Array.Empty<decimal>(), 100m);

        Assert.True(poor.UnitCostPerBaseUnit > full.UnitCostPerBaseUnit);
    }

    [Fact]
    public void Zero_output_cannot_be_costed()
    {
        Assert.Throws<InvalidAssignmentException>(() =>
            CostingCalculator.ForProduction(new[] { 1m }, Array.Empty<decimal>(), 0m));
    }

    [Fact]
    public void Margin_is_measured_on_price()
    {
        var (margin, percent) = CostingCalculator.Margin(price: 200m, cost: 150m);

        Assert.Equal(50m, margin);
        Assert.Equal(25m, percent);
    }

    [Fact]
    public void Zero_price_has_no_margin_percent()
    {
        Assert.Null(CostingCalculator.Margin(price: 0m, cost: 10m).MarginPercent);
    }
}