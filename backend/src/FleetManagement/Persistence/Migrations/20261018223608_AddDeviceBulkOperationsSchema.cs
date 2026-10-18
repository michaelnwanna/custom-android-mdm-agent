using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetManagement.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceBulkOperationsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Manufacturer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OsVersion = table.Column<int>(type: "integer", nullable: true),
                    FirstEnrolledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    AccessDisabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.DeviceId);
                    table.ForeignKey(
                        name: "FK_Devices_PendingEnrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "PendingEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeviceTags",
                columns: table => new
                {
                    TagId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceTags", x => x.TagId);
                });

            migrationBuilder.CreateTable(
                name: "DeviceTagAssignments",
                columns: table => new
                {
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemovedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceTagAssignments", x => x.AssignmentId);
                    table.ForeignKey(
                        name: "FK_DeviceTagAssignments_DeviceTags_TagId",
                        column: x => x.TagId,
                        principalTable: "DeviceTags",
                        principalColumn: "TagId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceTagAssignments_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "DeviceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_AccessDisabled",
                table: "Devices",
                column: "AccessDisabled");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_EnrollmentId",
                table: "Devices",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_LastSeenAt",
                table: "Devices",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_State",
                table: "Devices",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTagAssignments_Device_Tag_Active",
                table: "DeviceTagAssignments",
                columns: new[] { "DeviceId", "TagId", "RemovedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTagAssignments_DeviceId",
                table: "DeviceTagAssignments",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTagAssignments_RemovedAt",
                table: "DeviceTagAssignments",
                column: "RemovedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTagAssignments_TagId",
                table: "DeviceTagAssignments",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTags_IsActive",
                table: "DeviceTags",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTags_Name_Unique",
                table: "DeviceTags",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceTagAssignments");

            migrationBuilder.DropTable(
                name: "DeviceTags");

            migrationBuilder.DropTable(
                name: "Devices");
        }
    }
}
