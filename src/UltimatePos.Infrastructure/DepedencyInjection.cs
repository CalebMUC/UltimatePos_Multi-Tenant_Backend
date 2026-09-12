using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Identity;
using UltimatePos.Infrastructure.Auth;
using UltimatePos.Infrastructure.Persistence;
using UltimatePos.Infrastructure.Configuration;
using UltimatePos.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;



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

            services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();

            services.AddScoped<ITokenService, JwtTokenService>();

            return services;

        }
    }
}
