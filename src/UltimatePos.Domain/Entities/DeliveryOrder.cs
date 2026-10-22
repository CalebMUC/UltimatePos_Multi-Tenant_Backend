using System;
using System.Collections.Generic;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Domain.Entities
{
    /// <summary>
    /// Pending → OutForDelivery → Delivered, or Cancelled from either open state (a called-off delivery), or Returned once out for
    /// delivery (the rider came back with the goods: nobody home, refused). Stored as text, so new states go at the end.
    /// </summary>
    public enum DeliveryOrderStatus { Pending, OutForDelivery, Delivered, Cancelled, Returned }

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
        /// <summary>How the customer said they will pay (chosen at the till when the order was charged): Cash to collect, an M-Pesa
        /// code, a till payment or credit. A guide only — the payment recorded on delivery is what counts. Null on older orders.</summary>
        public PaymentMethod? ExpectedPaymentMethod { get; set; }

        /// <summary>Who took it out. A name, not a user: riders need not sign in.</summary>
        public string? RiderName { get; set; }
        public DateTime? DispatchedAt { get; set; }
        public Guid? DispatchedBy { get; set; }
        /// <summary>The trip it went out on. Orders dispatched together with one rider share a trip.</summary>
        public Guid? DeliveryTripId { get; set; }

        /// <summary>The sale it became. Set, with DeliveredAt and DeliveredBy, in the sale's own transaction.</summary>
        public Guid? SaleId { get; set; }
        public DateTime? DeliveredAt { get; set; }
        /// <summary>The user who recorded the delivery (typically a cashier, once the rider is back).</summary>
        public Guid? DeliveredBy { get; set; }

        public DateTime? CancelledAt { get; set; }
        public Guid? CancelledBy { get; set; }
        public string? CancelReason { get; set; }

        /// <summary>Returned: the rider brought the goods back undelivered. Nothing had left stock, so the reservation is released.</summary>
        public DateTime? ReturnedAt { get; set; }
        public Guid? ReturnedBy { get; set; }
        public string? ReturnReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }

        public Customer Customer { get; set; } = null!;
        public Branch Branch { get; set; } = null!;
        public Sale? Sale { get; set; }
        public DeliveryTrip? Trip { get; set; }
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
        /// <summary>Set on the lines of a bundle ordered as one item: one line per part, each reserving its own stock. The sale on
        /// delivery is rung up with the bundle (BundleQuantity of it), so it is priced as the bundle, not as the parts.</summary>
        public Guid? ProductBundleId { get; set; }
        public decimal? BundleQuantity { get; set; }

        public DeliveryOrder DeliveryOrder { get; set; } = null!;
        public Product Product { get; set; } = null!;
        public UnitOfMeasure UnitOfMeasure { get; set; } = null!;
        public ProductBundle? ProductBundle { get; set; }
    }

    /// <summary>
    /// One run out by one rider: the orders dispatched together. Counting trips (not orders) is what fuel is compared against, and a trip
    /// whose orders all came back is a wasted trip.
    /// </summary>
    public class DeliveryTrip : ITenantEntity, IBranchEntity
    {
        public Guid DeliveryTripId { get; set; }
        public Guid BusinessId { get; set; }
        public Guid BranchId { get; set; }
        /// <summary>TRP-000001 ...</summary>
        public string TripNumber { get; set; } = string.Empty;
        public string RiderName { get; set; } = string.Empty;
        public DateTime DispatchedAt { get; set; } = DateTime.UtcNow;
        public Guid? DispatchedBy { get; set; }

        public ICollection<DeliveryOrder> Orders { get; set; } = new List<DeliveryOrder>();
    }
}
