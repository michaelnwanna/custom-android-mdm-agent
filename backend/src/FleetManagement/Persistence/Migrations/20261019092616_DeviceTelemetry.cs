using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetManagement.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeviceTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_telemetry",
                columns: table => new
                {
                    telemetry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    battery_level = table.Column<int>(type: "integer", nullable: false),
                    is_charging = table.Column<bool>(type: "boolean", nullable: false),
                    storage_used_mb = table.Column<long>(type: "bigint", nullable: false),
                    storage_total_mb = table.Column<long>(type: "bigint", nullable: false),
                    storage_free_mb = table.Column<long>(type: "bigint", nullable: false),
                    available_memory_mb = table.Column<long>(type: "bigint", nullable: false),
                    total_memory_mb = table.Column<long>(type: "bigint", nullable: false),
                    connectivity = table.Column<int>(type: "integer", nullable: false),
                    wifi_signal_strength_dbm = table.Column<int>(type: "integer", nullable: true),
                    cellular_signal_strength_dbm = table.Column<int>(type: "integer", nullable: true),
                    cpu_usage_percent = table.Column<int>(type: "integer", nullable: false),
                    last_check_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    snapshot_taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_telemetry", x => x.telemetry_id);
                    table.ForeignKey(
                        name: "fk_device_telemetry_device",
                        column: x => x.device_id,
                        principalTable: "Devices",
                        principalColumn: "DeviceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "device_health_alerts",
                columns: table => new
                {
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_type = table.Column<int>(type: "integer", nullable: false),
                    severity = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    telemetry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_health_alerts", x => x.alert_id);
                    table.ForeignKey(
                        name: "fk_device_health_alerts_device",
                        column: x => x.device_id,
                        principalTable: "Devices",
                        principalColumn: "DeviceId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_device_health_alerts_telemetry",
                        column: x => x.telemetry_id,
                        principalTable: "device_telemetry",
                        principalColumn: "telemetry_id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_health_alerts_created",
                table: "device_health_alerts",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_device_health_alerts_device_active",
                table: "device_health_alerts",
                columns: new[] { "device_id", "resolved_at" });

            migrationBuilder.CreateIndex(
                name: "ix_device_health_alerts_device_created",
                table: "device_health_alerts",
                columns: new[] { "device_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_device_health_alerts_resolved",
                table: "device_health_alerts",
                column: "resolved_at");

            migrationBuilder.CreateIndex(
                name: "ix_device_health_alerts_severity_active",
                table: "device_health_alerts",
                columns: new[] { "severity", "resolved_at" });

            migrationBuilder.CreateIndex(
                name: "IX_device_health_alerts_telemetry_id",
                table: "device_health_alerts",
                column: "telemetry_id");

            migrationBuilder.CreateIndex(
                name: "ix_device_health_alerts_type_active",
                table: "device_health_alerts",
                columns: new[] { "alert_type", "resolved_at" });

            migrationBuilder.CreateIndex(
                name: "ix_device_telemetry_battery",
                table: "device_telemetry",
                columns: new[] { "device_id", "battery_level" });

            migrationBuilder.CreateIndex(
                name: "ix_device_telemetry_connectivity",
                table: "device_telemetry",
                columns: new[] { "device_id", "connectivity" });

            migrationBuilder.CreateIndex(
                name: "ix_device_telemetry_created",
                table: "device_telemetry",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_device_telemetry_device_created",
                table: "device_telemetry",
                columns: new[] { "device_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_device_telemetry_device_snapshot",
                table: "device_telemetry",
                columns: new[] { "device_id", "snapshot_taken_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_health_alerts");

            migrationBuilder.DropTable(
                name: "device_telemetry");
        }
    }
}
