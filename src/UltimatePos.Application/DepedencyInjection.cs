using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Accounting;
using UltimatePos.Application.Business;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Identity;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Production;
using UltimatePos.Application.Purchasing;
using UltimatePos.Application.Sales;

namespace UltimatePos.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            // Feature services and validators register here as each area is built.
            services.AddScoped<AuthService>();
            services.AddScoped<BusinessService>();
            services.AddScoped<CatalogService>();
            services.AddScoped<PurchasingService>();
            services.AddScoped<InventoryService>();
            services.AddScoped<ProductionService>();
            services.AddScoped<SalesService>(); 
            services.AddScoped<AccountingService>();

            return services;
        }
    }
}
