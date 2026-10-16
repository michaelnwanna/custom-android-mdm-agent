using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetManagement.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnrollmentSigningAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SigningAttempted",
                table: "PendingEnrollments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Enrollment_SigningAttempt",
                table: "PendingEnrollments",
                sql: "NOT \"SigningAttempted\" OR \"OperationId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Enrollment_SigningAttempt",
                table: "PendingEnrollments");

            migrationBuilder.DropColumn(
                name: "SigningAttempted",
                table: "PendingEnrollments");
        }
    }
}
