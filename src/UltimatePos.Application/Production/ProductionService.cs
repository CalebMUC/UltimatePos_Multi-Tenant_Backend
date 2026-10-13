using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Costing;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Production.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Production
{
    public class ProductionService
    {
        private const int QuantityDecimals = 4;
        private const int MaxAdditionalCostLines = 20;

        private readonly IProductionRepository _repository;
        private readonly ICatalogRepository _catalogRepository;
        private readonly IInventoryRepository _inventoryRepository;
        private readonly ICurrentUser _currentUser;

        public ProductionService(
            IProductionRepository repository, ICatalogRepository catalogRepository,
            IInventoryRepository inventoryRepository, ICurrentUser currentUser)
        {
            _repository = repository;
            _catalogRepository = catalogRepository;
            _inventoryRepository = inventoryRepository;
            _currentUser = currentUser;
        }

        // ---- Formulas ----

        public async Task<FormulaDto> CreateFormulaAsync(CreateFormulaRequestDto request)
        {
            var lines = request.Lines.ToList();

            if (request.OutputQuantity <= 0)
                throw new InvalidAssignmentException("Output quantity must be greater than zero.");
            if (lines.Count == 0)
                throw new InvalidAssignmentException("A formula needs at least one ingredient line.");
            if (lines.Any(l => l.Quantity <= 0))
                throw new InvalidAssignmentException("Ingredient quantity must be greater than zero on every line.");
            if (lines.GroupBy(l => l.IngredientProductId).Any(g => g.Count() > 1))
                throw new InvalidAssignmentException("The same ingredient appears on more than one line — combine them into one.");
            if (lines.Any(l => l.IngredientProductId == request.ProductId))
                throw new InvalidAssignmentException("A product cannot be an ingredient of its own formula.");

            var ids = lines.Select(l => l.IngredientProductId).Append(request.ProductId).Distinct().ToList();
            var products = (await _catalogRepository.GetProductsByIdsAsync(ids)).ToDictionary(p => p.ProductId);

            if (!products.TryGetValue(request.ProductId, out var output))
                throw new NotFoundException($"Product '{request.ProductId}' not found.");
            if (output.ItemType != ItemType.FinishedGood)
                throw new InvalidAssignmentException("A formula can only produce a FinishedGood product.");
            if (await _catalogRepository.GetConversionFactorAsync(request.ProductId, request.OutputUnitOfMeasureId) is null)
                throw new InvalidAssignmentException("The output product has no conversion for the output unit given.");

            foreach (var line in lines)
            {
                if (!products.TryGetValue(line.IngredientProductId, out var ingredient))
                    throw new NotFoundException($"Ingredient product '{line.IngredientProductId}' not found.");
                if (!ingredient.IsActive)
                    throw new InvalidAssignmentException($"Ingredient '{ingredient.Name}' is inactive.");
                if (await _catalogRepository.GetConversionFactorAsync(line.IngredientProductId, line.UnitOfMeasureId) is null)
                    throw new InvalidAssignmentException($"Ingredient '{ingredient.Name}' has no conversion for the unit given.");
            }

            var formula = new Formula
            {
                FormulaId = Guid.NewGuid(),
                ProductId = request.ProductId,
                OutputQuantity = request.OutputQuantity,
                OutputUnitOfMeasureId = request.OutputUnitOfMeasureId,
                Notes = request.Notes,
                CreatedBy = _currentUser.UserId,
                Lines = lines.Select(l => new FormulaLine
                {
                    IngredientProductId = l.IngredientProductId,
                    UnitOfMeasureId = l.UnitOfMeasureId,
                    Quantity = l.Quantity,
                    CreatedBy = _currentUser.UserId
                }).ToList()
            };

            var created = await _repository.CreateFormulaVersionAsync(formula, _currentUser.UserId);
            return await GetFormulaByIdAsync(created.FormulaId);
        }

        public async Task<IEnumerable<FormulaDto>> GetFormulasAsync(Guid? productId, bool activeOnly) =>
            (await _repository.GetFormulasAsync(productId, activeOnly)).Select(ToDto);

        public async Task<FormulaDto> GetFormulaByIdAsync(Guid formulaId)
        {
            var formula = await _repository.GetFormulaByIdAsync(formulaId)
                ?? throw new NotFoundException($"Formula '{formulaId}' not found.");
            return ToDto(formula);
        }

        public async Task<FormulaDto> DeactivateFormulaAsync(Guid formulaId)
        {
            await _repository.DeactivateFormulaAsync(formulaId, _currentUser.UserId);
            return await GetFormulaByIdAsync(formulaId);
        }

        // ---- Production runs ----

        public async Task<ProductionPreviewDto> PreviewProductionRunAsync(CreateProductionRunRequestDto request)
        {
            var plan = await BuildPlanAsync(request.FormulaId, request.QuantityToProduce, request.UnitOfMeasureId);

            //return new ProductionPreviewDto(
            //    plan.Formula.FormulaId, plan.Formula.VersionNumber, plan.Formula.ProductId, plan.Formula.Product.Name,
            //    request.QuantityToProduce, request.UnitOfMeasureId,
            //    plan.Requirements.All(r => r.Shortfall == 0),
            //    plan.Requirements.Select(r => new ProductionRequirementDto(
            //        r.Line.IngredientProductId, r.Line.IngredientProduct.Name, r.Line.IngredientProduct.Sku, r.Line.UnitOfMeasureId,
            //        r.RequiredInLineUnit, r.RequiredBase, r.AvailableBase, r.Shortfall)));

            var additionalCosts = BuildAdditionalCosts(request.AdditionalCosts, null);

            var output = request.ActualQuantityProduced ?? request.QuantityToProduce;
            if (output <= 0)
                throw new InvalidAssignmentException("Actual quantity produced must be greater than zero.");

            // Estimate only: stock is costed at today's average, and the real figure is fixed inside the run's transaction.
            var material = plan.Requirements.Sum(r => r.EstimatedCost);
            var additional = additionalCosts.Sum(c => c.Amount);
            var total = material + additional;
            var costPerUnit = CostingCalculator.RoundMoney(total / output);

            var priceChecks = new List<ProductionPriceCheckDto>();
            foreach (var priceType in Enum.GetValues<PriceType>())
            {
                var price = await _catalogRepository.GetActivePriceAsync(plan.Formula.ProductId, request.UnitOfMeasureId, priceType);
                if (!price.HasValue) continue;
                var (margin, percent) = CostingCalculator.Margin(price.Value, costPerUnit);
                priceChecks.Add(new ProductionPriceCheckDto(priceType.ToString(), price.Value, margin, percent));
            }

            return new ProductionPreviewDto(
                plan.Formula.FormulaId, plan.Formula.VersionNumber, plan.Formula.ProductId, plan.Formula.Product.Name,
                request.QuantityToProduce, request.UnitOfMeasureId,
                plan.Requirements.All(r => r.Shortfall == 0),
                plan.Requirements.Select(r => new ProductionRequirementDto(
                    r.Line.IngredientProductId, r.Line.IngredientProduct.Name, r.Line.IngredientProduct.Sku, r.Line.UnitOfMeasureId,
                    r.RequiredInLineUnit, r.RequiredBase, r.AvailableBase, r.Shortfall, r.UnitCost, r.EstimatedCost)),
                material, additional, total, costPerUnit,
                plan.Requirements.All(r => r.UnitCost > 0), priceChecks);
        }

        public async Task<ProductionRunDto> RecordProductionRunAsync(CreateProductionRunRequestDto request)
        {
            var plan = await BuildPlanAsync(request.FormulaId, request.QuantityToProduce, request.UnitOfMeasureId);

            var additionalCosts = BuildAdditionalCosts(request.AdditionalCosts, _currentUser.UserId);

            var shortages = plan.Requirements.Where(r => r.Shortfall > 0).ToList();
            if (shortages.Count > 0)
                throw new InsufficientStockException(
                    "Insufficient stock to produce this batch (base units): " +
                    string.Join("; ", shortages.Select(s =>
                        $"{s.Line.IngredientProduct.Name} — need {s.RequiredBase}, have {s.AvailableBase}, short {s.Shortfall}")));

            // A run can only be costed if everything it consumes has a cost. Producing anyway would enter the finished good
            // into stock cheaper than it really is, and every margin report after that would be wrong.
            var uncosted = plan.Requirements.Where(r => r.UnitCost <= 0).ToList();
            if (uncosted.Count > 0)
                throw new InvalidAssignmentException(
                    "This batch cannot be costed — no cost is recorded for: " +
                    string.Join(", ", uncosted.Select(u => u.Line.IngredientProduct.Name)) +
                    ". Receive them on a purchase order with a unit cost, or set a cost with POST /costing/revaluations.");

            var actual = request.ActualQuantityProduced ?? request.QuantityToProduce;
            if (actual <= 0)
                throw new InvalidAssignmentException("Actual quantity produced must be greater than zero.");

            var outputInBaseUnits = Round(actual * plan.OutputFactor);
            if (outputInBaseUnits <= 0)
                throw new InvalidAssignmentException("Quantity produced is too small — it rounds to zero in the product's base unit.");

            // Number generated only after validation passes, so ordinary failures don't burn run numbers.
            var number = await _repository.GetNextDocumentNumberAsync("PR");
            var runId = Guid.NewGuid();
            var userId = _currentUser.UserId;

            var run = new ProductionRun
            {
                ProductionRunId = runId,
                RunNumber = $"PR-{number:D6}",
                FormulaId = plan.Formula.FormulaId,
                ProductId = plan.Formula.ProductId,
                UnitOfMeasureId = request.UnitOfMeasureId,
                QuantityPlanned = request.QuantityToProduce,
                QuantityProduced = actual,
                Notes = request.Notes,
                CreatedBy = userId,
                // The repository finalises every cost figure inside the stock transaction, where the average cost is locked.
                AdditionalCost = additionalCosts.Sum(c => c.Amount),
                AdditionalCosts = additionalCosts,
                Inputs = plan.Requirements.Select(r => new ProductionRunInput
                {
                    IngredientProductId = r.Line.IngredientProductId,
                    UnitOfMeasureId = r.Line.UnitOfMeasureId,
                    QuantityConsumed = r.RequiredInLineUnit,
                    QuantityConsumedInBaseUnits = r.RequiredBase,
                    CreatedBy = userId
                }).ToList()
            };

            var movements = plan.Requirements.Select(r => new StockMovement
            {
                ProductId = r.Line.IngredientProductId,
                MovementType = StockMovementType.ProductionConsumption,
                QuantityChange = -r.RequiredBase,
                ReferenceType = "ProductionRun",
                ReferenceId = runId,
                BatchId = runId,
                Notes = request.Notes,
                CreatedBy = userId
            }).ToList();

            movements.Add(new StockMovement
            {
                ProductId = plan.Formula.ProductId,
                MovementType = StockMovementType.ProductionOutput,
                QuantityChange = outputInBaseUnits,
                ReferenceType = "ProductionRun",
                ReferenceId = runId,
                BatchId = runId,
                Notes = request.Notes,
                CreatedBy = userId
            });

            await _repository.RecordProductionRunAsync(run, movements);
            return await GetProductionRunByIdAsync(runId);
        }

        public async Task<PagedResult<ProductionRunDto>> GetProductionRunsAsync(int page, int pageSize, Guid? productId)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var (items, totalCount) = await _repository.GetProductionRunsAsync(page, pageSize, productId);
            return new PagedResult<ProductionRunDto>(items.Select(ToDto), page, pageSize, totalCount);
        }

        public async Task<ProductionRunDto> GetProductionRunByIdAsync(Guid productionRunId)
        {
            var run = await _repository.GetProductionRunByIdAsync(productionRunId)
                ?? throw new NotFoundException($"Production run '{productionRunId}' not found.");
            return ToDto(run);
        }

        // ---- Planning (shared by preview and record, so what you preview is exactly what runs) ----

        private sealed record RequirementLine(FormulaLine Line, decimal RequiredInLineUnit, decimal RequiredBase, decimal AvailableBase, decimal UnitCost)
        {
            public decimal Shortfall => Math.Max(0, RequiredBase - AvailableBase);
            public decimal EstimatedCost => CostingCalculator.LineCost(RequiredBase, UnitCost);
        }

        private sealed record ProductionPlan(Formula Formula, decimal OutputFactor, IReadOnlyList<RequirementLine> Requirements);

        private async Task<ProductionPlan> BuildPlanAsync(Guid formulaId, decimal quantityToProduce, Guid unitOfMeasureId)
        {
            if (quantityToProduce <= 0)
                throw new InvalidAssignmentException("Quantity to produce must be greater than zero.");

            var formula = await _repository.GetFormulaByIdAsync(formulaId)
                ?? throw new NotFoundException($"Formula '{formulaId}' not found.");

            if (!formula.IsActive)
                throw new InvalidAssignmentException($"Formula version {formula.VersionNumber} is retired — production must use the active version.");

            var outputFactor = await _catalogRepository.GetConversionFactorAsync(formula.ProductId, unitOfMeasureId)
                ?? throw new InvalidAssignmentException("The output product has no conversion for the unit given.");
            var formulaOutputFactor = await _catalogRepository.GetConversionFactorAsync(formula.ProductId, formula.OutputUnitOfMeasureId)
                ?? throw new InvalidAssignmentException("The formula's output unit no longer has a valid conversion.");

            // How many "standard batches" this run is, measured in the product's base unit so mixed units compare correctly.
            var scale = (quantityToProduce * outputFactor) / (formula.OutputQuantity * formulaOutputFactor);

            //var onHand = await _inventoryRepository.GetQuantitiesOnHandAsync(formula.Lines.Select(l => l.IngredientProductId));

            var ingredientIds = formula.Lines.Select(l => l.IngredientProductId).ToList();
            var onHand = await _inventoryRepository.GetQuantitiesOnHandAsync(ingredientIds);
            var averageCosts = await _inventoryRepository.GetAverageCostsAsync(ingredientIds);

            var requirements = new List<RequirementLine>();
            foreach (var line in formula.Lines)
            {
                var factor = await _catalogRepository.GetConversionFactorAsync(line.IngredientProductId, line.UnitOfMeasureId)
                    ?? throw new InvalidAssignmentException($"Ingredient '{line.IngredientProduct.Name}' no longer has a valid conversion for its formula unit.");

                var requiredInLineUnit = Round(line.Quantity * scale);
                var requiredBase = Round(line.Quantity * scale * factor);
                if (requiredBase <= 0)
                    throw new InvalidAssignmentException($"Quantity is too small — '{line.IngredientProduct.Name}' rounds to zero.");

                //onHand.TryGetValue(line.IngredientProductId, out var available);
                //requirements.Add(new RequirementLine(line, requiredInLineUnit, requiredBase, available));

                onHand.TryGetValue(line.IngredientProductId, out var available);
                averageCosts.TryGetValue(line.IngredientProductId, out var unitCost);
                requirements.Add(new RequirementLine(line, requiredInLineUnit, requiredBase, available, unitCost));
            }

            return new ProductionPlan(formula, outputFactor, requirements);
        }

        private static decimal Round(decimal value) => Math.Round(value, QuantityDecimals, MidpointRounding.AwayFromZero);

        private static List<ProductionRunCost> BuildAdditionalCosts(IEnumerable<ProductionCostRequestDto>? costs, Guid? userId)
        {
            var list = costs?.ToList() ?? new List<ProductionCostRequestDto>();
            if (list.Count > MaxAdditionalCostLines)
                throw new InvalidAssignmentException($"A run can have at most {MaxAdditionalCostLines} additional cost lines.");

            foreach (var c in list)
            {
                if (!Enum.IsDefined(c.CostType))
                    throw new InvalidAssignmentException("Unknown cost type.");
                if (c.Amount <= 0)
                    throw new InvalidAssignmentException("Each additional cost must be greater than zero.");
                if (c.Description is { Length: > 200 })
                    throw new InvalidAssignmentException("A cost description cannot exceed 200 characters.");
            }

            return list.Select(c => new ProductionRunCost
            {
                CostType = c.CostType,
                Description = string.IsNullOrWhiteSpace(c.Description) ? null : c.Description.Trim(),
                Amount = CostingCalculator.RoundMoney(c.Amount),
                CreatedBy = userId
            }).ToList();
        }

        // ---- Mapping ----

        private static FormulaDto ToDto(Formula f) =>
            new(f.FormulaId, f.ProductId, f.Product.Name, f.Product.Sku, f.VersionNumber, f.OutputQuantity, f.OutputUnitOfMeasureId,
                f.Notes, f.IsActive, f.CreatedAt,
                f.Lines.OrderBy(l => l.IngredientProduct.Name)
                       .Select(l => new FormulaLineDto(l.FormulaLineId, l.IngredientProductId, l.IngredientProduct.Name, l.IngredientProduct.Sku, l.UnitOfMeasureId, l.Quantity)));

        private static ProductionRunDto ToDto(ProductionRun r) =>
            new(r.ProductionRunId, r.RunNumber, r.FormulaId, r.Formula.VersionNumber, r.ProductId, r.Product.Name, r.UnitOfMeasureId,
                r.QuantityPlanned, r.QuantityProduced,
                r.QuantityPlanned == 0 ? 0 : Math.Round(r.QuantityProduced / r.QuantityPlanned * 100m, 2),
                r.Notes, r.CreatedAt,
                r.Inputs.Select(i => new ProductionRunInputDto(i.IngredientProductId, i.UnitOfMeasureId, i.QuantityConsumed, i.QuantityConsumedInBaseUnits, i.UnitCost, i.TotalCost)),
                r.MaterialCost, r.AdditionalCost, r.TotalCost, r.UnitCost,
                r.QuantityProduced == 0 ? 0 : CostingCalculator.RoundMoney(r.TotalCost / r.QuantityProduced),
                r.AdditionalCosts.Select(c => new ProductionRunCostDto(c.CostType.ToString(), c.Description, c.Amount)));
    }
}
