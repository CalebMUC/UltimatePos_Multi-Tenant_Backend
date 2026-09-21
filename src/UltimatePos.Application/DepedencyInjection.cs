using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Business;
using UltimatePos.Application.Identity;

namespace UltimatePos.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            // Feature services and validators register here as each area is built.
            services.AddScoped<AuthService>();
            services.AddScoped<BusinessService>();
            return services;
        }
    }
}
