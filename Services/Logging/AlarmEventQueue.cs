using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Ptlk.AlarmLogger.Configuration;

namespace Ptlk.AlarmLogger.Services.Logging;

public sealed record AlarmEventEnvelope(string Channel, string Payload, DateTimeOffset ReceivedAt);

public sealed class AlarmEventQueue
{
    private readonly Channel<AlarmEventEnvelope> _channel;
    private int _count;
    private long _droppedCount;

    public AlarmEventQueue(IOptions<AlarmLoggerOptions> options)
    {
        _channel = Channel.CreateBounded<AlarmEventEnvelope>(new BoundedChannelOptions(options.Value.EventQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public int Count => Volatile.Read(ref _count);

    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    public async ValueTask<bool> EnqueueAsync(
        AlarmEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _channel.Writer.WriteAsync(envelope, cancellationToken);
            Interlocked.Increment(ref _count);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Interlocked.Increment(ref _droppedCount);
            return false;
        }
    }

    public async IAsyncEnumerable<AlarmEventEnvelope> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var envelope in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            Interlocked.Decrement(ref _count);
            yield return envelope;
        }
    }
}
