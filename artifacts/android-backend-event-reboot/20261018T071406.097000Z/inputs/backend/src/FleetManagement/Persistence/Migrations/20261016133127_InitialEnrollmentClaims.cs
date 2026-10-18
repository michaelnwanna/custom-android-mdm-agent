using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetManagement.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialEnrollmentClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CsrSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubjectPublicKeyInfoBase64 = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingEnrollments", x => x.Id);
                    table.CheckConstraint("CK_Enrollment_Claim", "(\"OperationId\" IS NULL AND \"DeviceId\" IS NULL AND \"CsrSha256\" IS NULL AND \"SubjectPublicKeyInfoBase64\" IS NULL AND \"ClaimedAt\" IS NULL) OR (\"OperationId\" IS NOT NULL AND \"DeviceId\" IS NOT NULL AND \"CsrSha256\" IS NOT NULL AND \"SubjectPublicKeyInfoBase64\" IS NOT NULL AND \"ClaimedAt\" IS NOT NULL AND \"OperationId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"DeviceId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"CsrSha256\" ~ '^[0-9a-f]{64}$' AND length(\"SubjectPublicKeyInfoBase64\") > 0 AND \"ClaimedAt\" >= \"IssuedAt\" AND \"ClaimedAt\" < \"ExpiresAt\")");
                    table.CheckConstraint("CK_Enrollment_Digest", "\"TokenDigest\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_Enrollment_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Enrollment_Lifetime", "\"ExpiresAt\" > \"IssuedAt\" AND \"ExpiresAt\" <= \"IssuedAt\" + interval '15 minutes'");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingEnrollments_DeviceId",
                table: "PendingEnrollments",
                column: "DeviceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingEnrollments_OperationId",
                table: "PendingEnrollments",
                column: "OperationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingEnrollments");
        }
    }
}
