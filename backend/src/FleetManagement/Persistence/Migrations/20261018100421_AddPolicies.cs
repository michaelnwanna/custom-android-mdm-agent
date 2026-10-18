using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetManagement.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Policies",
                columns: table => new
                {
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Creator = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CurrentVersion = table.Column<int>(type: "integer", nullable: false),
                    PolicyJson = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Policies", x => x.PolicyId);
                });

            migrationBuilder.CreateTable(
                name: "PolicyAssignments",
                columns: table => new
                {
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedVersion = table.Column<int>(type: "integer", nullable: false),
                    AcknowledgedVersion = table.Column<int>(type: "integer", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyAssignments", x => x.AssignmentId);
                    table.ForeignKey(
                        name: "FK_PolicyAssignments_Policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policies",
                        principalColumn: "PolicyId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PolicyAssignments_PendingEnrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "PendingEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Policies_IsActive",
                table: "Policies",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_IsActive_ModifiedAt",
                table: "Policies",
                columns: new[] { "IsActive", "ModifiedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyAssignments_EnrollmentId",
                table: "PolicyAssignments",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyAssignments_EnrollmentId_AssignedAt",
                table: "PolicyAssignments",
                columns: new[] { "EnrollmentId", "AssignedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PolicyAssignments");

            migrationBuilder.DropTable(
                name: "Policies");
        }
    }
}
