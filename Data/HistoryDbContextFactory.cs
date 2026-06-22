using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;
using Ptlk.AlarmLogger.Configuration;

namespace Ptlk.AlarmLogger.Data;

public sealed class HistoryDbContextFactory : IDesignTimeDbContextFactory<HistoryDbContext>
{
    public HistoryDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<HistoryDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("ALARM_LOGGER_HISTORY_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=redis_logger_db;Username=redis_logger;Password=change_me";

        optionsBuilder.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "alarm_logger"))
            .UseSnakeCaseNamingConvention();

        return new HistoryDbContext(optionsBuilder.Options, Options.Create(new AlarmLoggerOptions()));
    }
}
