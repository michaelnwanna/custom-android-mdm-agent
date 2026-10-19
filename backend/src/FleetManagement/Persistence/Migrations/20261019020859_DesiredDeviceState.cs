using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetManagement.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DesiredDeviceState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DesiredDeviceStates",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentPolicyId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentPolicyVersion = table.Column<long>(type: "bigint", nullable: true),
                    DesiredReleaseIds = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false, defaultValue: ""),
                    AssignedTagIds = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false, defaultValue: ""),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ComputationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsValid = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesiredDeviceStates", x => x.DeviceId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DesiredDeviceStates_ComputedAt",
                table: "DesiredDeviceStates",
                column: "ComputedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DesiredDeviceStates_ExpiresAt",
                table: "DesiredDeviceStates",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_DesiredDeviceStates_IsValid",
                table: "DesiredDeviceStates",
                column: "IsValid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DesiredDeviceStates");
        }
    }
}
