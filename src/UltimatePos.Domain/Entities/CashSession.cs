using System;
using System.Collections.Generic;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Domain.Entities
{
    /// <summary>
    /// A branch's cash drawer for one stretch of trading, usually a day. It opens with the float allocated for giving change and closes
    /// with the cash counted. What should be in the drawer is worked out from the float, the cash taken (sales, customer payments), the
    /// cash paid out (refunds, expenses, supplier payments) and the pay-ins and pay-outs recorded during the day; the difference from
    /// the count is the variance. One open session per branch.
    /// </summary>
    public class CashSession : ITenantEntity, IBranchEntity
    {
        public Guid CashSessionId { get; set; }
        public Guid BusinessId { get; set; }
        public Guid BranchId { get; set; }
        /// <summary>CSH-000001 ...</summary>
        public string SessionNumber { get; set; } = string.Empty;
        public CashSessionStatus Status { get; set; } = CashSessionStatus.Open;

        /// <summary>The float put in the drawer for giving change.</summary>
        public decimal OpeningFloat { get; set; }
        public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
        public Guid? OpenedBy { get; set; }
        public string? OpeningNotes { get; set; }

        /// <summary>Set on closing, fixed from then on: what the drawer should have held, what was counted and the difference
        /// (counted - expected; negative = short).</summary>
        public decimal? ExpectedCash { get; set; }
        public decimal? CountedCash { get; set; }
        public decimal? Variance { get; set; }
        public DateTime? ClosedAt { get; set; }
        public Guid? ClosedBy { get; set; }
        public string? ClosingNotes { get; set; }

        public Branch Branch { get; set; } = null!;
        public ICollection<CashSessionMovement> Movements { get; set; } = new List<CashSessionMovement>();
    }

    /// <summary>Cash put into or taken out of the drawer that is not a sale, payment or expense: a float top-up, banking, change given to
    /// a rider and handed back.</summary>
    public class CashSessionMovement
    {
        public Guid CashSessionMovementId { get; set; }
        public Guid CashSessionId { get; set; }
        public CashMovementType MovementType { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }

        public CashSession CashSession { get; set; } = null!;
    }
}
