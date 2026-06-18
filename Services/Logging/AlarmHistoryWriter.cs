using Microsoft.EntityFrameworkCore;
using Ptlk.AlarmLogger.Data;
using Ptlk.AlarmLogger.Models;
using Ptlk.AlarmLogger.Services.Status;

namespace Ptlk.AlarmLogger.Services.Logging;

public sealed class AlarmHistoryWriter(
    IDbContextFactory<HistoryDbContext> dbFactory,
    AlarmLoggerRuntimeSnapshotService status,
    ILogger<AlarmHistoryWriter> logger)
{
    public async Task WriteBatchAsync(
        IReadOnlyList<AlarmHistoryRecord> records,
        CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            db.AlarmHistoryRecords.AddRange(records);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            status.MarkWriteSuccess(records.Count);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogWarning(ex, "Failed to write AlarmLogger history batch.");
            status.MarkWriteFailure(records.Count, ex.Message);
        }
    }
}
