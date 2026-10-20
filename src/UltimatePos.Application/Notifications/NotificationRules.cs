using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Notifications
{
    [Flags]
    public enum NotificationChannels
    {
        None = 0,
        InApp = 1,
        Email = 2,
        Sms = 4,
    }

    /// <summary>
    /// Who hears about an event, and how. Recipients are chosen by permission in the event's business, never by name, so a role
    /// change is all it takes to start or stop someone receiving an alert.
    /// </summary>
    /// <param name="RecipientPermissions">Users holding ANY of these (in the business, or through a platform role) are told.</param>
    /// <param name="Channels">How permission-holders are told.</param>
    /// <param name="SubjectChannels">How the user the event is about is told (None = not told).</param>
    /// <param name="ExcludeActor">Don't tell the person who did it.</param>
    /// <param name="Link">Frontend path; {entityId} is replaced with the event's entity id.</param>
    public sealed record NotificationRule(
        string EventType,
        string Title,
        IReadOnlyList<string> RecipientPermissions,
        NotificationChannels Channels = NotificationChannels.InApp,
        NotificationChannels SubjectChannels = NotificationChannels.None,
        bool ExcludeActor = false,
        string? Link = null);

    /// <summary>
    /// The routing table. An event type with no rule is recorded in the audit log only (sign-ins, sign-outs, adjustments...).
    /// Email and SMS only go out when those channels are enabled in NotificationSettings and the user has an address or number.
    /// </summary>
    public static class NotificationRules
    {
        private static readonly string[] StockManagers = { "PurchaseOrders.Create", "Stock.Adjust" };

        public static readonly IReadOnlyList<NotificationRule> All = new NotificationRule[]
        {
            // Stock
            new(SystemEventTypes.StockBelowReorderLevel, "Low stock", StockManagers, Link: "/inventory?productId={entityId}"),
            new(SystemEventTypes.StockOutOfStock, "Out of stock", StockManagers,
                NotificationChannels.InApp | NotificationChannels.Email, Link: "/inventory?productId={entityId}"),
            new(SystemEventTypes.BatchExpiringSoon, "Batch expiring soon", new[] { "Stock.Adjust" }, Link: "/inventory/batches?batchId={entityId}"),
            new(SystemEventTypes.BatchExpired, "Batch expired", new[] { "Stock.Adjust" },
                NotificationChannels.InApp | NotificationChannels.Email, Link: "/inventory/batches?batchId={entityId}"),
            new(SystemEventTypes.StockRevalued, "Stock revalued", new[] { "Costing.View" }, ExcludeActor: true, Link: "/inventory/valuation"),

            // Branches and transfers. Transfer events carry the branch that must act: the destination on dispatch, the source on receipt.
            new(SystemEventTypes.BranchCreated, "New branch", new[] { "Branches.Manage" }, ExcludeActor: true, Link: "/branches/{entityId}"),
            new(SystemEventTypes.UserBranchesChanged, "Your branches changed", Array.Empty<string>(), SubjectChannels: NotificationChannels.InApp),
            new(SystemEventTypes.StockTransferDispatched, "Stock on its way", new[] { "StockTransfers.Receive" }, ExcludeActor: true,
                Link: "/stock-transfers/{entityId}"),
            new(SystemEventTypes.StockTransferReceived, "Transfer received", new[] { "StockTransfers.Dispatch" }, ExcludeActor: true,
                Link: "/stock-transfers/{entityId}"),

            // Purchasing
            new(SystemEventTypes.PurchaseOrderOverdue, "Purchase order overdue", new[] { "PurchaseOrders.Receive", "PurchaseOrders.Create" },
                Link: "/purchase-orders/{entityId}"),

            // Sales and M-Pesa
            new(SystemEventTypes.SaleVoided, "Sale voided", new[] { "Sales.Void" }, ExcludeActor: true, Link: "/sales/{entityId}"),
            new(SystemEventTypes.SaleCreditLimitOverridden, "Credit limit overridden", new[] { "Sales.OverrideCreditLimit" },
                ExcludeActor: true, Link: "/sales/{entityId}"),
            new(SystemEventTypes.MpesaStkPaymentFailed, "M-Pesa payment failed", Array.Empty<string>(),
                SubjectChannels: NotificationChannels.InApp, Link: "/sales/{entityId}"),
            new(SystemEventTypes.MpesaTillPaymentUnmatched, "Unmatched M-Pesa payment", new[] { "Mpesa.View" }, Link: "/mpesa"),
            new(SystemEventTypes.PaymentPromiseOverdue, "Promised payment overdue", new[] { "Customers.RecordPayment" }, Link: "/sales/{entityId}"),
            new(SystemEventTypes.ExpenseVoided, "Expense voided", new[] { "Expenses.Void" }, ExcludeActor: true, Link: "/expenses/{entityId}"),

            // Users and security
            new(SystemEventTypes.UserCreated, "New user", new[] { "Users.View" },
                SubjectChannels: NotificationChannels.Email, ExcludeActor: true, Link: "/users/{entityId}"),
            new(SystemEventTypes.UserDeactivated, "User deactivated", new[] { "Users.View" }, ExcludeActor: true, Link: "/users/{entityId}"),
            new(SystemEventTypes.UserRolesChanged, "Your access changed", Array.Empty<string>(), SubjectChannels: NotificationChannels.InApp),
            new(SystemEventTypes.UserAccountLocked, "Account locked", new[] { "Users.Lock" },
                NotificationChannels.InApp | NotificationChannels.Email,
                SubjectChannels: NotificationChannels.Email | NotificationChannels.Sms, Link: "/users/{entityId}"),
            new(SystemEventTypes.UserPasswordReset, "Your password was reset", Array.Empty<string>(),
                SubjectChannels: NotificationChannels.InApp | NotificationChannels.Email | NotificationChannels.Sms),
            new(SystemEventTypes.UserPasswordChanged, "Your password was changed", Array.Empty<string>(),
                SubjectChannels: NotificationChannels.Email),
            new(SystemEventTypes.RolePermissionsChanged, "Role permissions changed", new[] { "Roles.View" }, ExcludeActor: true, Link: "/roles/{entityId}"),

            // Platform: business status and licences. Business admins (Businesses.Update) hear about their own business;
            // super users through the platform permissions.
            new(SystemEventTypes.BusinessRegistered, "New business registered", new[] { "Businesses.ViewAll" }, ExcludeActor: true,
                Link: "/platform/businesses/{entityId}"),
            new(SystemEventTypes.BusinessOwnerAdded, "You were added to a business", Array.Empty<string>(),
                SubjectChannels: NotificationChannels.InApp | NotificationChannels.Email),
            new(SystemEventTypes.BusinessSuspended, "Business suspended", new[] { "Businesses.Update", "Businesses.Suspend" },
                NotificationChannels.InApp | NotificationChannels.Email, ExcludeActor: true),
            new(SystemEventTypes.BusinessReactivated, "Business reactivated", new[] { "Businesses.Update", "Businesses.Suspend" },
                NotificationChannels.InApp | NotificationChannels.Email, ExcludeActor: true),
            new(SystemEventTypes.BusinessClosed, "Business closed", new[] { "Businesses.Update", "Businesses.Deactivate" },
                NotificationChannels.InApp | NotificationChannels.Email, ExcludeActor: true),
            new(SystemEventTypes.BusinessReopened, "Business reopened", new[] { "Businesses.Update", "Businesses.Deactivate" },
                NotificationChannels.InApp | NotificationChannels.Email, ExcludeActor: true),
            new(SystemEventTypes.LicenseIssued, "Licence issued", new[] { "Businesses.Update", "Licenses.View" },
                NotificationChannels.InApp | NotificationChannels.Email, ExcludeActor: true, Link: "/settings/license"),
            new(SystemEventTypes.LicenseCancelled, "Licence cancelled", new[] { "Businesses.Update", "Licenses.View" },
                NotificationChannels.InApp | NotificationChannels.Email, ExcludeActor: true, Link: "/settings/license"),
            new(SystemEventTypes.LicenseExpiringSoon, "Licence expiring soon", new[] { "Businesses.Update", "Licenses.Manage" },
                NotificationChannels.InApp | NotificationChannels.Email, Link: "/settings/license"),
            new(SystemEventTypes.LicenseExpired, "Licence expired", new[] { "Businesses.Update", "Licenses.Manage" },
                NotificationChannels.InApp | NotificationChannels.Email, Link: "/settings/license"),
            new(SystemEventTypes.SuperUserCreated, "New super user", new[] { "Platform.Users.Manage" },
                SubjectChannels: NotificationChannels.Email, ExcludeActor: true),
            new(SystemEventTypes.SupportPasswordReset, "Your password was reset", Array.Empty<string>(),
                SubjectChannels: NotificationChannels.InApp | NotificationChannels.Email | NotificationChannels.Sms),
        };

        private static readonly Dictionary<string, NotificationRule> ByType = All.ToDictionary(r => r.EventType, StringComparer.Ordinal);

        public static NotificationRule? For(string eventType) => ByType.GetValueOrDefault(eventType);
    }
}
