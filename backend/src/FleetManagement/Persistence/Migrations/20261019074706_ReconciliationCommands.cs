using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetManagement.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReconciliationCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReconciliationCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuditLogId = table.Column<Guid>(type: "uuid", nullable: true),
                    CommandType = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 50),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PayloadJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ScheduledFor = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastStatusUpdate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveryAttempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MaxDeliveryAttempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    ExecutionResultJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationCommands", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationCommands_AuditLogId",
                table: "ReconciliationCommands",
                column: "AuditLogId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationCommands_CreatedAt",
                table: "ReconciliationCommands",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationCommands_DeviceId",
                table: "ReconciliationCommands",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationCommands_DeviceId_Status",
                table: "ReconciliationCommands",
                columns: new[] { "DeviceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationCommands_EnrollmentId",
                table: "ReconciliationCommands",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationCommands_Priority",
                table: "ReconciliationCommands",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationCommands_ReadyForDelivery",
                table: "ReconciliationCommands",
                columns: new[] { "Status", "Priority", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationCommands_Status",
                table: "ReconciliationCommands",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReconciliationCommands");
        }
    }
}
