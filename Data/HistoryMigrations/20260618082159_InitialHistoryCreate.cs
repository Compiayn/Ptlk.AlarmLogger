using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ptlk.AlarmLogger.Data.HistoryMigrations
{
    /// <inheritdoc />
    public partial class InitialHistoryCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "alarm_logger");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:timescaledb", ",,");

            migrationBuilder.CreateTable(
                name: "alarm_history_records",
                schema: "alarm_logger",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    event_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    condition_name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    condition_sub_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    condition_active = table.Column<bool>(type: "boolean", nullable: false),
                    quality = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    quality_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_acknowledge = table.Column<bool>(type: "boolean", nullable: false),
                    old_value_json = table.Column<string>(type: "jsonb", nullable: true),
                    new_value_json = table.Column<string>(type: "jsonb", nullable: true),
                    message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alarm_history_records", x => new { x.timestamp, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_alarm_history_records_condition_active",
                schema: "alarm_logger",
                table: "alarm_history_records",
                column: "condition_active");

            migrationBuilder.CreateIndex(
                name: "ix_alarm_history_records_is_acknowledge",
                schema: "alarm_logger",
                table: "alarm_history_records",
                column: "is_acknowledge");

            migrationBuilder.CreateIndex(
                name: "ix_alarm_history_records_quality",
                schema: "alarm_logger",
                table: "alarm_history_records",
                column: "quality");

            migrationBuilder.CreateIndex(
                name: "ix_alarm_history_records_source_name",
                schema: "alarm_logger",
                table: "alarm_history_records",
                column: "source_name");

            migrationBuilder.CreateIndex(
                name: "ix_alarm_history_records_source_name_timestamp",
                schema: "alarm_logger",
                table: "alarm_history_records",
                columns: new[] { "source_name", "timestamp" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_alarm_history_records_timestamp",
                schema: "alarm_logger",
                table: "alarm_history_records",
                column: "timestamp",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alarm_history_records",
                schema: "alarm_logger");
        }
    }
}
