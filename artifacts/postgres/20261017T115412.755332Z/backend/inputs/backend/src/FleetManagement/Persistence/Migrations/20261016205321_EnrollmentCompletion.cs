using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetManagement.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnrollmentCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompletedResponseJson",
                table: "PendingEnrollments",
                type: "character varying(100000)",
                maxLength: 100000,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Enrollment_Completion",
                table: "PendingEnrollments",
                sql: "\"CompletedResponseJson\" IS NULL OR (\"OperationId\" IS NOT NULL AND (jsonb_typeof(\"CompletedResponseJson\"::jsonb) = 'object' AND \"CompletedResponseJson\"::jsonb ->> 'schemaVersion' = '1' AND \"CompletedResponseJson\"::jsonb ->> 'deviceId' = \"DeviceId\"::text) IS TRUE)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Enrollment_Completion",
                table: "PendingEnrollments");

            migrationBuilder.DropColumn(
                name: "CompletedResponseJson",
                table: "PendingEnrollments");
        }
    }
}
