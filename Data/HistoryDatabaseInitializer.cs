using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ptlk.AlarmLogger.Configuration;

namespace Ptlk.AlarmLogger.Data;

public static class HistoryDatabaseInitializer
{
    private const string ProductVersion = "10.0.7";
    private const string InitialMigration = "20260618082159_InitialHistoryCreate";
    private const string AckCategoryMigration = "20260622100721_AddAckCategoryFields";

    public static async Task PrepareMigrationsAsync(
        HistoryDbContext db,
        IOptions<AlarmLoggerOptions> options,
        CancellationToken cancellationToken = default)
    {
        var schema = OptionsRegistration.IsSafeIdentifier(options.Value.HistorySchema)
            ? options.Value.HistorySchema
            : "alarm_logger";
        var quotedSchema = QuoteIdentifier(schema);

#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"CREATE SCHEMA IF NOT EXISTS {quotedSchema};",
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            $"""
            CREATE TABLE IF NOT EXISTS {quotedSchema}."__EFMigrationsHistory" (
                migration_id character varying(150) NOT NULL,
                product_version character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY (migration_id)
            );
            """,
            cancellationToken);

        await NormalizeMigrationHistoryColumnsAsync(db, schema, cancellationToken);
#pragma warning restore EF1002

        if (!await TableExistsAsync(db, schema, "alarm_history_records", cancellationToken))
        {
            return;
        }

        await MarkMigrationAppliedAsync(db, schema, InitialMigration, cancellationToken);

        if (await ColumnsExistAsync(
            db,
            schema,
            "alarm_history_records",
            ["category_tag", "need_ack"],
            cancellationToken))
        {
            await MarkMigrationAppliedAsync(db, schema, AckCategoryMigration, cancellationToken);
            await EnsureAckIndexesAsync(db, schema, cancellationToken);
        }
    }

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

    private static async Task<bool> TableExistsAsync(
        HistoryDbContext db,
        string schema,
        string tableName,
        CancellationToken cancellationToken)
    {
        var result = await db.Database.SqlQueryRaw<bool>(
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = {0}
                      AND table_name = {1}
                ) AS "Value"
                """,
                schema,
                tableName)
            .SingleAsync(cancellationToken);

        return result;
    }

    private static async Task<bool> ColumnsExistAsync(
        HistoryDbContext db,
        string schema,
        string tableName,
        IReadOnlyCollection<string> columnNames,
        CancellationToken cancellationToken)
    {
        var count = await db.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*)::int AS "Value"
                FROM information_schema.columns
                WHERE table_schema = {0}
                  AND table_name = {1}
                  AND column_name = ANY({2})
                """,
                schema,
                tableName,
                columnNames.ToArray())
            .SingleAsync(cancellationToken);

        return count == columnNames.Count;
    }

    private static async Task MarkMigrationAppliedAsync(
        HistoryDbContext db,
        string schema,
        string migrationId,
        CancellationToken cancellationToken)
    {
        var quotedSchema = QuoteIdentifier(schema);

#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"""
            INSERT INTO {quotedSchema}."__EFMigrationsHistory" (migration_id, product_version)
            SELECT @p0, @p1
            WHERE NOT EXISTS (
                SELECT 1
                FROM {quotedSchema}."__EFMigrationsHistory"
                WHERE migration_id = @p0
            );
            """,
            [migrationId, ProductVersion],
            cancellationToken);
#pragma warning restore EF1002
    }

    private static async Task NormalizeMigrationHistoryColumnsAsync(
        HistoryDbContext db,
        string schema,
        CancellationToken cancellationToken)
    {
        var schemaLiteral = QuoteLiteral(schema);
        var quotedSchema = QuoteIdentifier(schema);

#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"""
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = {schemaLiteral}
                      AND table_name = '__EFMigrationsHistory'
                      AND column_name = 'MigrationId'
                ) THEN
                    ALTER TABLE {quotedSchema}."__EFMigrationsHistory"
                        RENAME COLUMN "MigrationId" TO migration_id;
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = {schemaLiteral}
                      AND table_name = '__EFMigrationsHistory'
                      AND column_name = 'ProductVersion'
                ) THEN
                    ALTER TABLE {quotedSchema}."__EFMigrationsHistory"
                        RENAME COLUMN "ProductVersion" TO product_version;
                END IF;
            END $$;
            """,
            cancellationToken);
#pragma warning restore EF1002
    }

    private static async Task EnsureAckIndexesAsync(
        HistoryDbContext db,
        string schema,
        CancellationToken cancellationToken)
    {
        var quotedSchema = QuoteIdentifier(schema);

#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"""
            CREATE INDEX IF NOT EXISTS ix_alarm_history_records_category_tag
                ON {quotedSchema}.alarm_history_records (category_tag);
            CREATE INDEX IF NOT EXISTS ix_alarm_history_records_need_ack
                ON {quotedSchema}.alarm_history_records (need_ack);
            """,
            cancellationToken);
#pragma warning restore EF1002
    }

    private static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string QuoteLiteral(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
