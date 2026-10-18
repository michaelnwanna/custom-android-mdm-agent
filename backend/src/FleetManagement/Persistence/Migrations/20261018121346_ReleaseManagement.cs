using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetManagement.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseManagement : Migration
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
                    CurrentVersion = table.Column<long>(type: "bigint", nullable: false),
                    PolicyJson = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Policies", x => x.PolicyId);
                });

            migrationBuilder.CreateTable(
                name: "Releases",
                columns: table => new
                {
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    VersionCode = table.Column<long>(type: "bigint", nullable: false),
                    ApkSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApkSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MinSdkLevel = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MetadataJson = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                    MetadataSignature = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Releases", x => x.ReleaseId);
                });

            migrationBuilder.CreateTable(
                name: "PolicyAssignments",
                columns: table => new
                {
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyId1 = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedVersion = table.Column<long>(type: "bigint", nullable: false),
                    AcknowledgedVersion = table.Column<long>(type: "bigint", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastReportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyAssignments", x => x.AssignmentId);
                    table.ForeignKey(
                        name: "FK_PolicyAssignments_PendingEnrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "PendingEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PolicyAssignments_Policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policies",
                        principalColumn: "PolicyId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PolicyAssignments_Policies_PolicyId1",
                        column: x => x.PolicyId1,
                        principalTable: "Policies",
                        principalColumn: "PolicyId");
                });

            migrationBuilder.CreateTable(
                name: "ReleaseDeployments",
                columns: table => new
                {
                    DeploymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    RolloutPercentage = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Progress_TotalDevices = table.Column<int>(type: "integer", nullable: false),
                    Progress_PendingDevices = table.Column<int>(type: "integer", nullable: false),
                    Progress_DownloadingDevices = table.Column<int>(type: "integer", nullable: false),
                    Progress_InstallingDevices = table.Column<int>(type: "integer", nullable: false),
                    Progress_CompletedDevices = table.Column<int>(type: "integer", nullable: false),
                    Progress_FailedDevices = table.Column<int>(type: "integer", nullable: false),
                    Progress_LastUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseDeployments", x => x.DeploymentId);
                    table.ForeignKey(
                        name: "FK_ReleaseDeployments_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "ReleaseId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentDeviceStates",
                columns: table => new
                {
                    StateId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeploymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentDeviceStates", x => x.StateId);
                    table.ForeignKey(
                        name: "FK_DeploymentDeviceStates_PendingEnrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "PendingEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeploymentDeviceStates_ReleaseDeployments_DeploymentId",
                        column: x => x.DeploymentId,
                        principalTable: "ReleaseDeployments",
                        principalColumn: "DeploymentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentDeviceStates_DeploymentId_EnrollmentId",
                table: "DeploymentDeviceStates",
                columns: new[] { "DeploymentId", "EnrollmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentDeviceStates_EnrollmentId",
                table: "DeploymentDeviceStates",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentDeviceStates_Status",
                table: "DeploymentDeviceStates",
                column: "Status");

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

            migrationBuilder.CreateIndex(
                name: "IX_PolicyAssignments_PolicyId",
                table: "PolicyAssignments",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_PolicyAssignments_PolicyId1",
                table: "PolicyAssignments",
                column: "PolicyId1");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseDeployments_IsActive",
                table: "ReleaseDeployments",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseDeployments_ReleaseId",
                table: "ReleaseDeployments",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Releases_IsActive",
                table: "Releases",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Releases_IsActive_CreatedAt",
                table: "Releases",
                columns: new[] { "IsActive", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeploymentDeviceStates");

            migrationBuilder.DropTable(
                name: "PolicyAssignments");

            migrationBuilder.DropTable(
                name: "ReleaseDeployments");

            migrationBuilder.DropTable(
                name: "Policies");

            migrationBuilder.DropTable(
                name: "Releases");
        }
    }
}
