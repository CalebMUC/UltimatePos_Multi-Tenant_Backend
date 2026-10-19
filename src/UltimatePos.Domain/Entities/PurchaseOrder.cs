using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Domain.Entities
{
    public class PurchaseOrder : ITenantEntity, IBranchEntity
    {
        public Guid PurchaseOrderId { get; set; }
        public Guid BusinessId { get; set; }
        /// <summary>The branch the goods are delivered to and received into.</summary>
        public Guid BranchId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public Guid SupplierId { get; set; }
        public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
        public DateTime OrderDate { get; set; } = DateTime.UtcNow;
        public DateTime? ExpectedDeliveryDate { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }

        public Supplier Supplier { get; set; } = null!;
        public Branch Branch { get; set; } = null!;
        public ICollection<PurchaseOrderLine> Lines { get; set; } = new List<PurchaseOrderLine>();
    }
}
