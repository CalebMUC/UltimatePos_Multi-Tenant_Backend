using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Production
{
    public interface IProductionRepository
    {
        Task<int> GetNextDocumentNumberAsync(string prefix);

        // Assigns the next version number, retires the previously active version, inserts the new one — one transaction.
        Task<Formula> CreateFormulaVersionAsync(Formula formula, Guid? updatedBy);
        Task<IEnumerable<Formula>> GetFormulasAsync(Guid? productId, bool activeOnly);
        Task<Formula?> GetFormulaByIdAsync(Guid formulaId);
        Task DeactivateFormulaAsync(Guid formulaId, Guid? updatedBy);

        // Run record + inputs + every stock movement commit together or not at all.
        Task RecordProductionRunAsync(ProductionRun run, IEnumerable<StockMovement> movements);
        Task<(IEnumerable<ProductionRun> Items, int TotalCount)> GetProductionRunsAsync(int page, int pageSize, Guid? productId);
        Task<ProductionRun?> GetProductionRunByIdAsync(Guid productionRunId);
    }
}
