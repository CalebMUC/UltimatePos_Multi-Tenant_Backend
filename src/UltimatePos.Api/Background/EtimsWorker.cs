using Microsoft.Extensions.Options;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Etims;
using UltimatePos.Infrastructure.Etims;

namespace UltimatePos.Api.Background
{
    /// <summary>
    /// Every Etims:IntervalSeconds: for each active eTIMS device, inside its business, queues what is new at its branch and sends what
    /// is due (<see cref="EtimsSyncService"/>). Idle until the Apigee credentials are configured. One device failing never stops the rest.
    /// </summary>
    public sealed class EtimsWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly EtimsSettings _settings;
        private readonly EtimsOptions _options;
        private readonly ILogger<EtimsWorker> _logger;

        public EtimsWorker(IServiceScopeFactory scopes, EtimsSettings settings, IOptions<EtimsOptions> options, ILogger<EtimsWorker> logger)
        {
            _scopes = scopes;
            _settings = settings;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.IsConfigured)
            {
                _logger.LogWarning("eTIMS reporting is off: set Etims:ConsumerKey, Etims:ConsumerSecret and Etims:ApigeeAppId to turn it on.");
                return;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } // let startup (migrations, seeding) finish first
            catch (OperationCanceledException) { return; }

            var interval = TimeSpan.FromSeconds(Math.Max(5, _settings.IntervalSeconds));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    List<(Guid BusinessId, Guid DeviceId)> devices;
                    using (var scope = _scopes.CreateScope())
                        devices = await scope.ServiceProvider.GetRequiredService<IEtimsRepository>().GetActiveDevicesAnyBusinessAsync();

                    foreach (var (businessId, deviceId) in devices)
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        try
                        {
                            using var scope = _scopes.CreateScope();
                            using var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(businessId);
                            var result = await scope.ServiceProvider.GetRequiredService<EtimsSyncService>().RunAsync(deviceId, stoppingToken);
                            if (result.Queued + result.Sent + result.Failed > 0)
                                _logger.LogInformation("eTIMS device {DeviceId}: queued {Queued}, sent {Sent}, failed {Failed}, waiting {Waiting}",
                                    deviceId, result.Queued, result.Sent, result.Failed, result.Waiting);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "eTIMS pass failed for device {DeviceId} of business {BusinessId}", deviceId, businessId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "eTIMS pass failed");
                }

                try { await Task.Delay(interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
