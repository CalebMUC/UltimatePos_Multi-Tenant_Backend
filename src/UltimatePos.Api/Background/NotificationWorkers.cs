using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Notifications;

namespace UltimatePos.Api.Background
{
    /// <summary>
    /// Every few seconds: turns new events into notifications, then sends due email/SMS deliveries. Runs with nobody signed in; each
    /// pass gets its own scope (and so its own DbContext). Polling the outbox table keeps this simple and safe with one instance;
    /// a message broker can replace the poll later without touching the services that record events.
    /// </summary>
    public sealed class NotificationDispatchWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly NotificationSettings _settings;
        private readonly ILogger<NotificationDispatchWorker> _logger;

        public NotificationDispatchWorker(IServiceScopeFactory scopes, NotificationSettings settings, ILogger<NotificationDispatchWorker> logger)
        {
            _scopes = scopes;
            _settings = settings;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.DispatchIntervalSeconds));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    int dispatched, sent;
                    do
                    {
                        using var scope = _scopes.CreateScope();
                        dispatched = await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchPendingAsync(100, stoppingToken);
                        sent = await scope.ServiceProvider.GetRequiredService<DeliveryProcessor>().ProcessDueAsync(50, stoppingToken);
                    }
                    while ((dispatched == 100 || sent == 50) && !stoppingToken.IsCancellationRequested); // drain a backlog before sleeping
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Notification dispatch pass failed");
                }

                try { await Task.Delay(interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>Every SweepIntervalMinutes: runs the alert sweep for each active business, inside that business.</summary>
    public sealed class AlertSweepWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly NotificationSettings _settings;
        private readonly ILogger<AlertSweepWorker> _logger;

        public AlertSweepWorker(IServiceScopeFactory scopes, NotificationSettings settings, ILogger<AlertSweepWorker> logger)
        {
            _scopes = scopes;
            _settings = settings;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } // let startup (migrations, seeding) finish first
            catch (OperationCanceledException) { return; }

            var interval = TimeSpan.FromMinutes(Math.Max(1, _settings.SweepIntervalMinutes));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    List<Guid> businesses;
                    using (var scope = _scopes.CreateScope())
                        businesses = (await scope.ServiceProvider.GetRequiredService<IAlertSweepRepository>().GetActiveBusinessIdsAsync()).ToList();

                    foreach (var businessId in businesses)
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        try
                        {
                            using var scope = _scopes.CreateScope();
                            using var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(businessId);
                            var raised = await scope.ServiceProvider.GetRequiredService<AlertSweepService>().RunAsync();
                            if (raised > 0)
                                _logger.LogInformation("Alert sweep raised {Count} alert(s) for business {BusinessId}", raised, businessId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Alert sweep failed for business {BusinessId}", businessId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Alert sweep pass failed");
                }

                try { await Task.Delay(interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
