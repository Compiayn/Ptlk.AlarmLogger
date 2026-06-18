using Ptlk.AlarmLogger.Services.Logging;
using Ptlk.AlarmLogger.Services.Status;
using StackExchange.Redis;

namespace Ptlk.AlarmLogger.Services.Redis;

public sealed class RedisAlarmEventSubscriptionService(
    RedisConnectionFactory redis,
    AlarmEventQueue queue,
    AlarmLoggerRuntimeSnapshotService status,
    ILogger<RedisAlarmEventSubscriptionService> logger) : BackgroundService
{
    private const string ChannelPattern = "evt:alarm:*";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await redis.IsConnectedAsync(stoppingToken))
                {
                    throw new InvalidOperationException("Redis is not connected.");
                }

                var connection = await redis.GetConnectionAsync(stoppingToken);
                var subscriber = connection.GetSubscriber();
                var subscriptionQueue = await subscriber.SubscribeAsync(RedisChannel.Pattern(ChannelPattern));
                subscriptionQueue.OnMessage(message =>
                {
                    _ = Task.Run(async () =>
                    {
                        var receivedAt = DateTimeOffset.UtcNow;
                        try
                        {
                            var enqueued = await queue.EnqueueAsync(
                                new AlarmEventEnvelope(
                                    message.Channel.ToString(),
                                    message.Message.ToString(),
                                    receivedAt),
                                stoppingToken);
                            if (enqueued)
                            {
                                status.MarkReceived(receivedAt);
                            }
                            else
                            {
                                status.MarkDiagnostic("Alarm event queue rejected a message.");
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to enqueue alarm event from {Channel}", message.Channel);
                            status.MarkDiagnostic("Failed to enqueue alarm event.");
                        }
                    }, stoppingToken);
                });

                status.SetSubscriptionState(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                status.SetSubscriptionState(false);
                logger.LogWarning(ex, "Redis alarm event subscription failed; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        status.SetSubscriptionState(false);
        try
        {
            var connection = await redis.GetConnectionAsync(cancellationToken);
            await connection.GetSubscriber().UnsubscribeAsync(RedisChannel.Pattern(ChannelPattern));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to unsubscribe Redis alarm event pattern.");
        }

        await base.StopAsync(cancellationToken);
    }
}
