using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetManagement.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviceReconciliationAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceReconciliationAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReconciliationTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReconciliationType = table.Column<string>(type: "text", nullable: false),
                    ReconciliationStatus = table.Column<string>(type: "text", nullable: false),
                    DesiredPolicyIds = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ObservedPolicyIds = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DesiredReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ObservedReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    DesiredTagIds = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ObservedTagIds = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ConflictTypes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ConflictDetails = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    GeneratedCommandIds = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceReconciliationAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceReconciliationAuditLogs_CreatedAt",
                table: "DeviceReconciliationAuditLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceReconciliationAuditLogs_DeviceId",
                table: "DeviceReconciliationAuditLogs",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceReconciliationAuditLogs_DeviceId_Timestamp",
                table: "DeviceReconciliationAuditLogs",
                columns: new[] { "DeviceId", "ReconciliationTimestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceReconciliationAuditLogs_EnrollmentId",
                table: "DeviceReconciliationAuditLogs",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceReconciliationAuditLogs_ReconciliationTimestamp",
                table: "DeviceReconciliationAuditLogs",
                column: "ReconciliationTimestamp");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceReconciliationAuditLogs_Status",
                table: "DeviceReconciliationAuditLogs",
                column: "ReconciliationStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceReconciliationAuditLogs");
        }
    }
}
