using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ptlk.AlarmLogger.Data.HistoryMigrations
{
    /// <inheritdoc />
    public partial class AddAckCategoryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category_tag",
                schema: "alarm_logger",
                table: "alarm_history_records",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "enable_ack",
                schema: "alarm_logger",
                table: "alarm_history_records",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "need_ack",
                schema: "alarm_logger",
                table: "alarm_history_records",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_alarm_history_records_category_tag",
                schema: "alarm_logger",
                table: "alarm_history_records",
                column: "category_tag");

            migrationBuilder.CreateIndex(
                name: "ix_alarm_history_records_need_ack",
                schema: "alarm_logger",
                table: "alarm_history_records",
                column: "need_ack");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_alarm_history_records_category_tag",
                schema: "alarm_logger",
                table: "alarm_history_records");

            migrationBuilder.DropIndex(
                name: "ix_alarm_history_records_need_ack",
                schema: "alarm_logger",
                table: "alarm_history_records");

            migrationBuilder.DropColumn(
                name: "category_tag",
                schema: "alarm_logger",
                table: "alarm_history_records");

            migrationBuilder.DropColumn(
                name: "enable_ack",
                schema: "alarm_logger",
                table: "alarm_history_records");

            migrationBuilder.DropColumn(
                name: "need_ack",
                schema: "alarm_logger",
                table: "alarm_history_records");
        }
    }
}
