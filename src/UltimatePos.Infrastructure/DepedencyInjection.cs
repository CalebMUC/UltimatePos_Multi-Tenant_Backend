using Microsoft.EntityFrameworkCore;
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
using UltimatePos.Application.Identity;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Payments;
using UltimatePos.Application.Production;
using UltimatePos.Application.Purchasing;
using UltimatePos.Application.Sales;
using UltimatePos.Infrastructure.Auth;
using UltimatePos.Infrastructure.Configuration;
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
            services.AddScoped<IProductionRepository, ProductionRepository>();

            services.Configure<MpesaOptions>(configuration.GetSection("Mpesa"));
            services.AddMemoryCache();
            services.AddHttpClient<IMpesaClient, DarajaMpesaClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<MpesaOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
            });
            services.AddScoped<IMpesaRepository, MpesaRepository>();
            services.AddScoped<ISalesRepository, SalesRepository>();

            services.AddScoped<CustomerLedger>();
            services.AddScoped<IAccountingRepository, AccountingRepository>();

            return services;

        }
    }
}
