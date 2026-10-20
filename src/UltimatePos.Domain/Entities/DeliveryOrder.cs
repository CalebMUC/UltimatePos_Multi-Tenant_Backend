using System;
using System.Collections.Generic;

namespace UltimatePos.Domain.Entities
{
    /// <summary>
    /// Pending → OutForDelivery → Delivered, or Cancelled from either open state (a failed or called-off delivery).
    /// </summary>
    public enum DeliveryOrderStatus { Pending, OutForDelivery, Delivered, Cancelled }

    /// <summary>
    /// Goods a customer ordered for delivery. The order itself moves no stock and takes no money: while it is open its quantities are
    /// held back from other sales at its branch (reserved), and on delivery it becomes an ordinary sale, which is the one place stock
    /// leaves and payment (cash, a matched till payment, or credit) is recorded. That sale is created and the order marked Delivered in
    /// one transaction, so an order can never become two sales.
    /// </summary>
    public class DeliveryOrder : ITenantEntity, IBranchEntity
    {
        public Guid DeliveryOrderId { get; set; }
        public Guid BusinessId { get; set; }
        /// <summary>The branch that delivers it: its stock is reserved, and the sale is recorded there.</summary>
        public Guid BranchId { get; set; }
        /// <summary>DEL-000001 ...</summary>
        public string OrderNumber { get; set; } = string.Empty;
        /// <summary>Always a registered customer: a delivery needs a name, a phone and an address, and may end on credit.</summary>
        public Guid CustomerId { get; set; }
        public string DeliveryAddress { get; set; } = string.Empty;
        /// <summary>Who to call on arrival. Defaults to the customer's phone.</summary>
        public string? ContactPhone { get; set; }
        public string? Notes { get; set; }
        public DeliveryOrderStatus Status { get; set; } = DeliveryOrderStatus.Pending;
        /// <summary>What the order came to at the customer's prices when it was taken. The sale is priced again on delivery.</summary>
        public decimal EstimatedTotal { get; set; }

        /// <summary>Who took it out. A name, not a user: riders need not sign in.</summary>
        public string? RiderName { get; set; }
        public DateTime? DispatchedAt { get; set; }
        public Guid? DispatchedBy { get; set; }

        /// <summary>The sale it became. Set, with DeliveredAt and DeliveredBy, in the sale's own transaction.</summary>
        public Guid? SaleId { get; set; }
        public DateTime? DeliveredAt { get; set; }
        /// <summary>The user who recorded the delivery (typically a cashier, once the rider is back).</summary>
        public Guid? DeliveredBy { get; set; }

        public DateTime? CancelledAt { get; set; }
        public Guid? CancelledBy { get; set; }
        public string? CancelReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }

        public Customer Customer { get; set; } = null!;
        public Branch Branch { get; set; } = null!;
        public Sale? Sale { get; set; }
        public ICollection<DeliveryOrderLine> Lines { get; set; } = new List<DeliveryOrderLine>();

        /// <summary>An open order holds its quantities back from other sales.</summary>
        public bool IsOpen => Status is DeliveryOrderStatus.Pending or DeliveryOrderStatus.OutForDelivery;
    }

    public class DeliveryOrderLine
    {
        public Guid DeliveryOrderLineId { get; set; }
        public Guid DeliveryOrderId { get; set; }
        public Guid ProductId { get; set; }
        public Guid UnitOfMeasureId { get; set; }
        /// <summary>In UnitOfMeasureId: two 20L refills is 2.</summary>
        public decimal Quantity { get; set; }
        /// <summary>What is reserved while the order is open: 40 litres for two 20L refills.</summary>
        public decimal QuantityInBaseUnits { get; set; }

        public DeliveryOrder DeliveryOrder { get; set; } = null!;
        public Product Product { get; set; } = null!;
        public UnitOfMeasure UnitOfMeasure { get; set; } = null!;
    }
}
