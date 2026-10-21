using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Accounting;
using UltimatePos.Application.Common;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Payments;
using UltimatePos.Application.Business;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Costing;
using UltimatePos.Application.Dashboard;
using UltimatePos.Application.Deliveries;
using UltimatePos.Application.Expenses;
using UltimatePos.Application.Identity;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Notifications;
using UltimatePos.Application.Production;
using UltimatePos.Application.Purchasing;
using UltimatePos.Application.Reports;
using UltimatePos.Application.Sales;

namespace UltimatePos.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            // Feature services and validators register here as each area is built.
            // The business every query is scoped to — from the caller's token, or entered explicitly by webhooks and onboarding.
            services.AddScoped<ITenantContext, TenantContext>();
            // The branch the caller works at, and the branches they can see — from the token, or entered explicitly.
            services.AddScoped<IBranchContext, BranchContext>();
            services.AddScoped<Branches.BranchService>();
            services.AddScoped<StockTransferService>();
            services.AddScoped<DeliveryOrderService>();
            services.AddScoped<ProductBundleService>();

            services.AddScoped<AuthService>();
            services.AddScoped<BusinessService>();
            services.AddScoped<CatalogService>();
            services.AddScoped<PurchasingService>();
            services.AddScoped<InventoryService>();
            services.AddScoped<ProductionService>();
            services.AddScoped<SalesService>();
            services.AddScoped<MpesaService>();
            services.AddScoped<AccountingService>();
            services.AddScoped<CostingService>();
            services.AddScoped<ExpenseService>();
            services.AddScoped<ReportsService>();
            services.AddScoped<DashboardService>();

            // Events, notifications and the audit log. The dispatcher, delivery processor and sweep are driven by background workers.
            services.AddScoped<NotificationDispatcher>();
            services.AddScoped<DeliveryProcessor>();
            services.AddScoped<AlertSweepService>();
            services.AddScoped<NotificationService>();

            // Super users: onboarding, licences, suspension, performance, support.
            services.AddScoped<Platform.PlatformService>();

            return services;
        }
    }
}
