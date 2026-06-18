using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ptlk.AlarmLogger.Configuration;
using Ptlk.AlarmLogger.Models;

namespace Ptlk.AlarmLogger.Data;

public sealed class HistoryDbContext(
    DbContextOptions<HistoryDbContext> options,
    IOptions<AlarmLoggerOptions>? alarmLoggerOptions = null) : DbContext(options)
{
    private readonly string _schema = ResolveSchema(alarmLoggerOptions?.Value.HistorySchema);

    public DbSet<AlarmHistoryRecord> AlarmHistoryRecords => Set<AlarmHistoryRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(_schema);
        modelBuilder.HasPostgresExtension("timescaledb");

        modelBuilder.Entity<AlarmHistoryRecord>(entity =>
        {
            entity.ToTable("alarm_history_records", _schema);
            entity.HasKey(x => new { x.Timestamp, x.Id });
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.SourceName).HasMaxLength(256).IsRequired();
            entity.Property(x => x.ConditionName)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(x => x.ConditionSubName).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Quality)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(x => x.OldValueJson).HasColumnType("jsonb");
            entity.Property(x => x.NewValueJson).HasColumnType("jsonb");
            entity.Property(x => x.Message).HasMaxLength(1024).IsRequired();
            entity.HasIndex(x => x.Timestamp).IsDescending();
            entity.HasIndex(x => x.SourceName);
            entity.HasIndex(x => new { x.SourceName, x.Timestamp }).IsDescending(false, true);
            entity.HasIndex(x => x.ConditionActive);
            entity.HasIndex(x => x.IsAcknowledge);
            entity.HasIndex(x => x.Quality);
        });
    }

    private static string ResolveSchema(string? schema)
    {
        if (OptionsRegistration.IsSafeIdentifier(schema))
        {
            return schema!;
        }

        return "alarm_logger";
    }
}
