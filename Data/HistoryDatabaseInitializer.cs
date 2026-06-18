using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ptlk.AlarmLogger.Configuration;

namespace Ptlk.AlarmLogger.Data;

public static class HistoryDatabaseInitializer
{
    public static async Task InitializeTimescaleAsync(
        HistoryDbContext db,
        IOptions<AlarmLoggerOptions> options,
        CancellationToken cancellationToken = default)
    {
        var schema = OptionsRegistration.IsSafeIdentifier(options.Value.HistorySchema)
            ? options.Value.HistorySchema
            : "alarm_logger";
        var quotedSchema = QuoteIdentifier(schema);

        await db.Database.ExecuteSqlRawAsync(
            "CREATE EXTENSION IF NOT EXISTS timescaledb;",
            cancellationToken);

#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"CREATE SCHEMA IF NOT EXISTS {quotedSchema};",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            $"SELECT create_hypertable('{schema}.alarm_history_records', 'timestamp', if_not_exists => TRUE, migrate_data => TRUE);",
            cancellationToken);
#pragma warning restore EF1002
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
