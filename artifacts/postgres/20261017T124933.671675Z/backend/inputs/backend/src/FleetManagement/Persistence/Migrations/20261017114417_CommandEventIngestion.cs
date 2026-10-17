using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetManagement.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommandEventIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommandEventOwnerships",
                columns: table => new
                {
                    CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandEventOwnerships", x => x.CommandId);
                    table.UniqueConstraint("AK_CommandEventOwnerships_CommandId_DeviceId_CorrelationId", x => new { x.CommandId, x.DeviceId, x.CorrelationId });
                    table.ForeignKey(
                        name: "FK_CommandEventOwnerships_PendingEnrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "PendingEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoredCommandEvents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentJson = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                    AcknowledgementJson = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredCommandEvents", x => x.EventId);
                    table.ForeignKey(
                        name: "FK_StoredCommandEvents_CommandEventOwnerships_CommandId_Device~",
                        columns: x => new { x.CommandId, x.DeviceId, x.CorrelationId },
                        principalTable: "CommandEventOwnerships",
                        principalColumns: new[] { "CommandId", "DeviceId", "CorrelationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommandEventOwnerships_EnrollmentId",
                table: "CommandEventOwnerships",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_StoredCommandEvents_CommandId_DeviceId_CorrelationId",
                table: "StoredCommandEvents",
                columns: new[] { "CommandId", "DeviceId", "CorrelationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoredCommandEvents");

            migrationBuilder.DropTable(
                name: "CommandEventOwnerships");
        }
    }
}
