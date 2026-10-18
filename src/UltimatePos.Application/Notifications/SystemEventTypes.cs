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
        public const string MpesaStkPaymentFailed = "Mpesa.StkPaymentFailed";
        public const string MpesaTillPaymentUnmatched = "Mpesa.TillPaymentUnmatched";
        public const string CustomerPaymentRecorded = "Customer.PaymentRecorded";

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
        public static string StockLow(Guid productId) => $"stock-low:{productId}";
        public static string StockOut(Guid productId) => $"stock-out:{productId}";
        public static string BatchExpiring(Guid batchId) => $"batch-expiring:{batchId}";
        public static string BatchExpired(Guid batchId) => $"batch-expired:{batchId}";
        public static string PurchaseOrderOverdue(Guid purchaseOrderId) => $"po-overdue:{purchaseOrderId}";
        public static string TillPaymentUnmatched(Guid mpesaTransactionId) => $"mpesa-unmatched:{mpesaTransactionId}";
    }
}
