using Microsoft.Extensions.Options;
using Ptlk.AlarmLogger.Configuration;
using Ptlk.AlarmLogger.Models;
using Ptlk.AlarmLogger.Services.Redis;
using Ptlk.AlarmLogger.Services.Status;

namespace Ptlk.AlarmLogger.Services.Startup;

public sealed class StartupGateService(
    RedisConnectionFactory redis,
    AlarmLoggerRuntimeSnapshotService status,
    IOptions<StartupGateOptions> options,
    ILogger<StartupGateService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(options.Value.WaitInitializedTimeoutMs);
        var delay = options.Value.InitialRetryDelayMs;
        status.SetStartupState(AlarmLoggerServiceStatus.Starting, false, false);

        while (!stoppingToken.IsCancellationRequested)
        {
            var redisConnected = await redis.IsConnectedAsync(stoppingToken);
            var initialized = false;

            if (redisConnected)
            {
                try
                {
                    var database = await redis.GetDatabaseAsync(stoppingToken);
                    initialized = (await database.StringGetAsync(".initialized")).ToString() == "1";
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Startup gate Redis check failed");
                    status.MarkDiagnostic("Startup gate Redis check failed.");
                }
            }

            if (redisConnected && initialized)
            {
                status.SetStartupState(AlarmLoggerServiceStatus.Running, true, true);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                var reason = redisConnected
                    ? "Waiting for Asset .initialized = 1."
                    : "Waiting for Redis connection.";
                status.SetStartupState(AlarmLoggerServiceStatus.Degraded, redisConnected, initialized, reason);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            status.SetStartupState(AlarmLoggerServiceStatus.Starting, redisConnected, initialized);
            await Task.Delay(delay, stoppingToken);
            delay = Math.Min(delay * 2, options.Value.MaxRetryDelayMs);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        var snapshot = status.CreateSnapshot(0, 0);
        status.SetStartupState(
            AlarmLoggerServiceStatus.Stopping,
            snapshot.RedisConnected,
            snapshot.AssetInitialized);
        return base.StopAsync(cancellationToken);
    }
}
