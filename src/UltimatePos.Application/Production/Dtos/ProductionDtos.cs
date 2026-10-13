using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Production.Dtos
{
    public record CreateFormulaLineRequestDto(Guid IngredientProductId, Guid UnitOfMeasureId, decimal Quantity);
    public record CreateFormulaRequestDto(Guid ProductId, decimal OutputQuantity, Guid OutputUnitOfMeasureId, string? Notes, IEnumerable<CreateFormulaLineRequestDto> Lines);

    public record FormulaLineDto(Guid FormulaLineId, Guid IngredientProductId, string IngredientName, string IngredientSku, Guid UnitOfMeasureId, decimal Quantity);
    public record FormulaDto(Guid FormulaId, Guid ProductId, string ProductName, string ProductSku, int VersionNumber,
        decimal OutputQuantity, Guid OutputUnitOfMeasureId, string? Notes, bool IsActive, DateTime CreatedAt, IEnumerable<FormulaLineDto> Lines);

    //public record CreateProductionRunRequestDto(Guid FormulaId, decimal QuantityToProduce, Guid UnitOfMeasureId, decimal? ActualQuantityProduced, string? Notes);

    //public record ProductionRequirementDto(Guid IngredientProductId, string IngredientName, string IngredientSku, Guid UnitOfMeasureId,
    //    decimal RequiredQuantity, decimal RequiredInBaseUnits, decimal AvailableInBaseUnits, decimal ShortfallInBaseUnits);
    //public record ProductionPreviewDto(Guid FormulaId, int FormulaVersion, Guid ProductId, string ProductName,
    //    decimal QuantityToProduce, Guid UnitOfMeasureId, bool CanProduce, IEnumerable<ProductionRequirementDto> Requirements);

    //public record ProductionRunInputDto(Guid IngredientProductId, Guid UnitOfMeasureId, decimal QuantityConsumed, decimal QuantityConsumedInBaseUnits);
    //public record ProductionRunDto(Guid ProductionRunId, string RunNumber, Guid FormulaId, int FormulaVersion, Guid ProductId, string ProductName,
    //    Guid UnitOfMeasureId, decimal QuantityPlanned, decimal QuantityProduced, decimal YieldPercentage, string? Notes, DateTime CreatedAt,
    //    IEnumerable<ProductionRunInputDto> Inputs);



    /// <summary>A non-stock cost of the run: labour, utilities, transport, ... Stock items (ingredients, tracked packaging) are costed automatically.</summary>
    public record ProductionCostRequestDto(ProductionCostType CostType, string? Description, decimal Amount);
    public record CreateProductionRunRequestDto(Guid FormulaId, decimal QuantityToProduce, Guid UnitOfMeasureId, decimal? ActualQuantityProduced, string? Notes,
        IEnumerable<ProductionCostRequestDto>? AdditionalCosts = null);

    public record ProductionRequirementDto(Guid IngredientProductId, string IngredientName, string IngredientSku, Guid UnitOfMeasureId,
        decimal RequiredQuantity, decimal RequiredInBaseUnits, decimal AvailableInBaseUnits, decimal ShortfallInBaseUnits,
        decimal UnitCostPerBaseUnit, decimal EstimatedCost);
    public record ProductionPriceCheckDto(string PriceType, decimal Price, decimal Margin, decimal? MarginPercent);
    public record ProductionPreviewDto(Guid FormulaId, int FormulaVersion, Guid ProductId, string ProductName,
        decimal QuantityToProduce, Guid UnitOfMeasureId, bool CanProduce, IEnumerable<ProductionRequirementDto> Requirements,
        decimal EstimatedMaterialCost, decimal AdditionalCost, decimal EstimatedTotalCost, decimal EstimatedCostPerUnit,
        bool CostComplete, IEnumerable<ProductionPriceCheckDto> PriceChecks);

    public record ProductionRunInputDto(Guid IngredientProductId, Guid UnitOfMeasureId, decimal QuantityConsumed, decimal QuantityConsumedInBaseUnits,
        decimal UnitCostPerBaseUnit, decimal TotalCost);
    public record ProductionRunCostDto(string CostType, string? Description, decimal Amount);
    public record ProductionRunDto(Guid ProductionRunId, string RunNumber, Guid FormulaId, int FormulaVersion, Guid ProductId, string ProductName,
        Guid UnitOfMeasureId, decimal QuantityPlanned, decimal QuantityProduced, decimal YieldPercentage, string? Notes, DateTime CreatedAt,
        IEnumerable<ProductionRunInputDto> Inputs,
        decimal MaterialCost, decimal AdditionalCost, decimal TotalCost, decimal CostPerBaseUnit, decimal CostPerUnit,
        IEnumerable<ProductionRunCostDto> AdditionalCosts);
}
