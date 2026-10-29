using Microsoft.EntityFrameworkCore;
using UltimatePos.Infrastructure.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Accounting;
using UltimatePos.Application.Business;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Deliveries;
using UltimatePos.Application.Etims;
using UltimatePos.Application.Costing;
using UltimatePos.Application.Expenses;
using UltimatePos.Application.Identity;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Payments;
using UltimatePos.Application.Production;
using UltimatePos.Application.Purchasing;
using UltimatePos.Application.Reports;
using UltimatePos.Application.Sales;
using UltimatePos.Infrastructure.Auth;
using UltimatePos.Infrastructure.Configuration;
using UltimatePos.Infrastructure.Etims;
using UltimatePos.Infrastructure.Import;
using UltimatePos.Infrastructure.Payments;
using UltimatePos.Infrastructure.Persistence;
using UltimatePos.Infrastructure.Persistence.Repositories;



namespace UltimatePos.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            // DbContext, repositories, HTTP clients, and Quartz jobs register here as
            // each area is built.
            services.AddDbContext<UltimatePosDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

            services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));

            services.AddScoped<IAuthRepository, AuthenticationRepository>();
            services.AddScoped<IBusinessRepository, BusinessRepository>();

            services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();

            services.AddScoped<ITokenService, JwtTokenService>();

            services.AddScoped<ICatalogRepository, CatalogRepository>();
            services.AddScoped<ICategoryImportParser, ClosedXmlCategoryImportParser>();
            services.AddScoped<IProductImportParser, ClosedXmlProductImportParser>();

            services.AddScoped<IInventoryRepository, InventoryRepository>();
            services.AddScoped<IPurchasingRepository, PurchasingRepository>();

            services.AddScoped<StockLedger>();
            services.AddScoped<Application.Branches.IBranchRepository, BranchRepository>();
            services.AddScoped<IStockTransferRepository, StockTransferRepository>();
            services.AddScoped<IDeliveryOrderRepository, DeliveryOrderRepository>();
            services.AddScoped<Application.CashSessions.ICashSessionRepository, CashSessionRepository>();
            services.AddScoped<Application.Customers.ICustomerInsightsRepository, CustomerInsightsRepository>();
            services.AddScoped<IProductBundleRepository, ProductBundleRepository>();
            services.AddScoped<IProductionRepository, ProductionRepository>();

            services.Configure<MpesaOptions>(configuration.GetSection("Mpesa"));
            services.AddMemoryCache();
            services.AddHttpClient<IMpesaClient, DarajaMpesaClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<MpesaOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
            });
            services.AddScoped<IMpesaRepository, MpesaRepository>();

            // KRA eTIMS (OSCU): one Apigee app for the platform; each branch device adds its own PIN, branch id and cmcKey.
            services.Configure<EtimsOptions>(configuration.GetSection("Etims"));
            services.AddSingleton(configuration.GetSection("Etims").Get<EtimsSettings>() ?? new EtimsSettings());
            services.AddHttpClient<IEtimsClient, KraEtimsClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<EtimsOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.TimeoutSeconds));
            });
            services.AddScoped<IEtimsRepository, EtimsRepository>();
            services.AddScoped<ISalesRepository, SalesRepository>();

            services.AddScoped<CustomerLedger>();
            services.AddScoped<SupplierLedger>();
            services.AddScoped<IAccountingRepository, AccountingRepository>();

            services.AddScoped<ICostingRepository, CostingRepository>();

            services.AddScoped<IExpenseRepository, ExpenseRepository>();

            services.AddScoped<IReportsRepository, ReportsRepository>();

            services.AddNotificationChannels(configuration);

            return services;

        }
    }
}
