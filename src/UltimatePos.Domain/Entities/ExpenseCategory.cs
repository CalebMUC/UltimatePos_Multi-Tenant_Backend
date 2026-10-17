using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Domain.Entities
{
    /// <summary>A heading operating expenses are grouped under in reports (Rent, Utilities, Salaries, ...).</summary>
    public class ExpenseCategory : ITenantEntity
    {
        public Guid ExpenseCategoryId { get; set; }
        public Guid BusinessId { get; set; }
        /// <summary>Short stable code, stored upper-case (e.g. RENT). Unique.</summary>
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }
    }

}
