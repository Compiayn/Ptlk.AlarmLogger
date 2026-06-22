using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptlk.AlarmLogger.Data.HistoryMigrations
{
    /// <inheritdoc />
    public partial class RemoveEnableAckFromAlarmHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE alarm_logger.alarm_history_records
                    DROP COLUMN IF EXISTS enable_ack;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE alarm_logger.alarm_history_records
                    ADD COLUMN IF NOT EXISTS enable_ack boolean NOT NULL DEFAULT true;
                """);
        }
    }
}
