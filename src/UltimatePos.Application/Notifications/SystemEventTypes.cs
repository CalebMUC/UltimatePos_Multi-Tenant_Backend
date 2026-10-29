namespace UltimatePos.Application.Notifications
{
    /// <summary>Every event the system records. "Area.WhatHappened". Each is in the audit log; NotificationRules decides who is told.</summary>
    public static class SystemEventTypes
    {
        // Identity
        public const string UserLoggedIn = "User.LoggedIn";
        public const string UserLoggedOut = "User.LoggedOut";
        public const string UserLoginFailed = "User.LoginFailed";
        public const string UserAccountLocked = "User.AccountLocked";
        public const string UserSwitchedBusiness = "User.SwitchedBusiness";
        public const string UserCreated = "User.Created";
        public const string UserUpdated = "User.Updated";
        public const string UserActivated = "User.Activated";
        public const string UserDeactivated = "User.Deactivated";
        public const string UserLocked = "User.Locked";
        public const string UserUnlocked = "User.Unlocked";
        public const string UserRolesChanged = "User.RolesChanged";
        public const string UserPasswordChanged = "User.PasswordChanged";
        public const string UserPasswordReset = "User.PasswordReset";
        public const string RoleCreated = "Role.Created";
        public const string RolePermissionsChanged = "Role.PermissionsChanged";

        // Business
        public const string BusinessRegistered = "Business.Registered";
        public const string BusinessProfileUpdated = "Business.ProfileUpdated";
        public const string BusinessSuspended = "Business.Suspended";
        public const string BusinessReactivated = "Business.Reactivated";
        public const string BusinessClosed = "Business.Closed";
        public const string BusinessReopened = "Business.Reopened";
        public const string BusinessSectorChanged = "Business.SectorChanged";
        public const string BusinessEtimsChanged = "Business.EtimsChanged";
        public const string BusinessOwnerAdded = "Business.OwnerAdded";

        // Licensing and the platform (super users)
        public const string LicenseIssued = "License.Issued";
        public const string LicenseCancelled = "License.Cancelled";
        public const string LicenseExpiringSoon = "License.ExpiringSoon";
        public const string LicenseExpired = "License.Expired";
        public const string SuperUserCreated = "Platform.SuperUserCreated";
        public const string SuperUserActivated = "Platform.SuperUserActivated";
        public const string SuperUserDeactivated = "Platform.SuperUserDeactivated";
        public const string SupportPasswordReset = "Platform.SupportPasswordReset";
        public const string SupportUserUnlocked = "Platform.SupportUserUnlocked";
        public const string SupportEtimsDeviceRegistered = "Platform.SupportEtimsDeviceRegistered";
        public const string SupportEtimsDeviceUpdated = "Platform.SupportEtimsDeviceUpdated";
        public const string SupportEtimsSubmissionRetried = "Platform.SupportEtimsSubmissionRetried";

        // Branches and transfers
        public const string BranchCreated = "Branch.Created";
        public const string BranchUpdated = "Branch.Updated";
        public const string BranchActivated = "Branch.Activated";
        public const string BranchDeactivated = "Branch.Deactivated";
        public const string UserBranchesChanged = "User.BranchesChanged";
        public const string UserSwitchedBranch = "User.SwitchedBranch";
        public const string StockTransferCreated = "StockTransfer.Created";
        public const string StockTransferDispatched = "StockTransfer.Dispatched";
        public const string StockTransferReceived = "StockTransfer.Received";
        public const string StockTransferCancelled = "StockTransfer.Cancelled";

        // Stock
        public const string StockBelowReorderLevel = "Stock.BelowReorderLevel";
        public const string StockOutOfStock = "Stock.OutOfStock";
        public const string StockReplenished = "Stock.Replenished";
        public const string StockAdjusted = "Stock.Adjusted";
        public const string StockRevalued = "Stock.Revalued";
        public const string BatchExpiringSoon = "Batch.ExpiringSoon";
        public const string BatchExpired = "Batch.Expired";

        // Sales and payments
        public const string SaleVoided = "Sale.Voided";
        public const string SaleReturned = "Sale.Returned";
        public const string SaleCreditLimitOverridden = "Sale.CreditLimitOverridden";
        /// <summary>A sale paid by an M-Pesa code the cashier typed, not checked with Safaricom: audit only, for reconciling against the statement.</summary>
        public const string SaleMpesaReferenceRecorded = "Sale.MpesaReferenceRecorded";
        public const string MpesaStkPaymentFailed = "Mpesa.StkPaymentFailed";
        public const string MpesaTillPaymentUnmatched = "Mpesa.TillPaymentUnmatched";
        public const string CustomerPaymentRecorded = "Customer.PaymentRecorded";
        public const string PaymentPromiseOverdue = "Customer.PaymentPromiseOverdue";
        /// <summary>A customer's standing discount was given, changed or removed.</summary>
        public const string CustomerDiscountChanged = "Customer.DiscountChanged";

        // Deliveries
        public const string DeliveryOrderCreated = "DeliveryOrder.Created";
        public const string DeliveryOrderDispatched = "DeliveryOrder.Dispatched";
        public const string DeliveryOrderDelivered = "DeliveryOrder.Delivered";
        public const string DeliveryOrderCancelled = "DeliveryOrder.Cancelled";
        /// <summary>The rider brought an order back undelivered.</summary>
        public const string DeliveryOrderReturned = "DeliveryOrder.Returned";

        // Cash drawer
        public const string CashSessionOpened = "CashSession.Opened";
        public const string CashSessionMovementRecorded = "CashSession.MovementRecorded";
        public const string CashSessionClosed = "CashSession.Closed";
        /// <summary>A drawer counted out short or over.</summary>
        public const string CashSessionVariance = "CashSession.Variance";

        // Purchasing and expenses
        public const string PurchaseOrderReceived = "PurchaseOrder.Received";
        public const string PurchaseOrderCancelled = "PurchaseOrder.Cancelled";
        public const string PurchaseOrderOverdue = "PurchaseOrder.Overdue";
        public const string SupplierPaymentRecorded = "Supplier.PaymentRecorded";
        public const string ExpenseVoided = "Expense.Voided";
    }

    /// <summary>Alert keys: one open alert per condition, so it is announced once until it clears.</summary>
    public static class AlertKeys
    {
        public static string StockLow(Guid productId, Guid branchId) => $"stock-low:{productId}:{branchId}";
        public static string StockOut(Guid productId, Guid branchId) => $"stock-out:{productId}:{branchId}";
        public static string BatchExpiring(Guid batchId) => $"batch-expiring:{batchId}";
        public static string BatchExpired(Guid batchId) => $"batch-expired:{batchId}";
        public static string PurchaseOrderOverdue(Guid purchaseOrderId) => $"po-overdue:{purchaseOrderId}";
        public static string TillPaymentUnmatched(Guid mpesaTransactionId) => $"mpesa-unmatched:{mpesaTransactionId}";
        /// <summary>A credit sale still unpaid after the day the customer promised to pay by.</summary>
        public static string PaymentPromiseOverdue(Guid saleId) => $"promise-overdue:{saleId}";
        /// <summary>Per licence period: renewing changes the end date, so the next period gets its own warning.</summary>
        public static string LicenseExpiring(DateOnly endsOn) => $"license-expiring:{endsOn:yyyyMMdd}";
        public static string LicenseExpired(DateOnly endsOn) => $"license-expired:{endsOn:yyyyMMdd}";
    }
}
