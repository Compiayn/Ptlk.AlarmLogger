using Ptlk.AlarmLogger.Models;
using Ptlk.AlarmLogger.Services.Logging;

namespace Ptlk.AlarmLogger.Services.Status;

public sealed class AlarmLoggerStatusQueryService(
    AlarmLoggerRuntimeSnapshotService runtime,
    AlarmEventQueue queue)
{
    public AlarmLoggerStatusSnapshot GetSnapshot() =>
        runtime.CreateSnapshot(queue.Count, queue.DroppedCount);
}
