using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UltimatePos.Application.Notifications;

namespace UltimatePos.Infrastructure.Notifications
{
    public static class NotificationChannelRegistration
    {
        /// <summary>
        /// Events, notifications and the email/SMS channels. The provider for each channel is chosen by configuration
        /// (Email:Provider, Sms:Provider); a new provider is one ISmsSender/IEmailSender class plus one line in the switch below.
        /// </summary>
        public static IServiceCollection AddNotificationChannels(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<ISystemEventRecorder, SystemEventRecorder>();
            services.AddScoped<INotificationRepository, NotificationRepository>();
            services.AddScoped<IAlertSweepRepository, AlertSweepRepository>();

            services.AddSingleton(configuration.GetSection("Notifications").Get<NotificationSettings>() ?? new NotificationSettings());
            services.Configure<EmailOptions>(configuration.GetSection("Email"));
            services.Configure<SmsOptions>(configuration.GetSection("Sms"));

            var emailProvider = configuration["Email:Provider"] ?? "Log";
            switch (emailProvider)
            {
                case "Smtp": services.AddScoped<IEmailSender, SmtpEmailSender>(); break;
                case "Log": services.AddScoped<IEmailSender, LogEmailSender>(); break;
                default: throw new InvalidOperationException($"Unknown Email:Provider '{emailProvider}'. Use 'Smtp' or 'Log'.");
            }

            var smsProvider = configuration["Sms:Provider"] ?? "Log";
            switch (smsProvider)
            {
                case "Log": services.AddScoped<ISmsSender, LogSmsSender>(); break;
                case "AfricasTalking":
                    throw new InvalidOperationException(
                        "Sms:Provider 'AfricasTalking' is not implemented yet — see AfricasTalkingOptions for how to add it. Use 'Log' meanwhile.");
                default: throw new InvalidOperationException($"Unknown Sms:Provider '{smsProvider}'. Use 'Log'.");
            }

            return services;
        }
    }
}
