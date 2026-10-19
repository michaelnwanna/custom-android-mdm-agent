using FleetManagement.Devices;
using FleetManagement.Enrollment;
using FleetManagement.Messaging;
using FleetManagement.Policies;
using FleetManagement.Releases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FleetManagement.Persistence;

public sealed class FleetDbContext(DbContextOptions<FleetDbContext> options) : DbContext(options)
{
    public DbSet<PendingEnrollment> PendingEnrollments => Set<PendingEnrollment>();
    public DbSet<EnrollmentProvisioningAudit> EnrollmentProvisioningAudits => Set<EnrollmentProvisioningAudit>();
    public DbSet<CommandEventOwnership> CommandEventOwnerships => Set<CommandEventOwnership>();
    public DbSet<StoredCommandEvent> StoredCommandEvents => Set<StoredCommandEvent>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<PolicyAssignment> PolicyAssignments => Set<PolicyAssignment>();
    public DbSet<Release> Releases => Set<Release>();
    public DbSet<ReleaseDeployment> ReleaseDeployments => Set<ReleaseDeployment>();
    public DbSet<DeploymentDeviceState> DeploymentDeviceStates => Set<DeploymentDeviceState>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceTag> DeviceTags => Set<DeviceTag>();
    public DbSet<DeviceTagAssignment> DeviceTagAssignments => Set<DeviceTagAssignment>();
    public DbSet<DesiredDeviceState> DesiredDeviceStates => Set<DesiredDeviceState>();
    public DbSet<DeviceReconciliationAuditLog> DeviceReconciliationAuditLogs => Set<DeviceReconciliationAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var ownership = modelBuilder.Entity<CommandEventOwnership>();
        ownership.ToTable("CommandEventOwnerships");
        ownership.HasKey(value => value.CommandId);
        ownership.Property(value => value.CommandId).ValueGeneratedNever();
        ownership.HasAlternateKey(value => new { value.CommandId, value.DeviceId, value.CorrelationId });
        ownership.HasOne<PendingEnrollment>().WithMany().HasForeignKey(value => value.EnrollmentId)
            .OnDelete(DeleteBehavior.Restrict);
        var events = modelBuilder.Entity<StoredCommandEvent>();
        events.ToTable("StoredCommandEvents");
        events.HasKey(value => value.EventId);
        events.Property(value => value.EventId).ValueGeneratedNever();
        events.Property(value => value.ContentJson).HasMaxLength(16384).IsRequired();
        events.Property(value => value.AcknowledgementJson).HasMaxLength(512).IsRequired();
        events.HasOne<CommandEventOwnership>().WithMany()
            .HasForeignKey(value => new { value.CommandId, value.DeviceId, value.CorrelationId })
            .HasPrincipalKey(value => new { value.CommandId, value.DeviceId, value.CorrelationId })
            .OnDelete(DeleteBehavior.Restrict);
        var audit = modelBuilder.Entity<EnrollmentProvisioningAudit>();
        audit.ToTable("EnrollmentProvisioningAudits");
        audit.HasKey(value => value.PendingEnrollmentId);
        audit.Property(value => value.PendingEnrollmentId).ValueGeneratedNever();
        audit.Property(value => value.Issuer).HasMaxLength(2048).IsRequired();
        audit.Property(value => value.Subject).HasMaxLength(256).IsRequired();
        audit.HasOne<PendingEnrollment>().WithMany().HasForeignKey(value => value.PendingEnrollmentId)
            .OnDelete(DeleteBehavior.Restrict);
        var enrollment = modelBuilder.Entity<PendingEnrollment>();
        enrollment.ToTable("PendingEnrollments", table =>
        {
            table.HasCheckConstraint("CK_Enrollment_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_Enrollment_Digest", "\"TokenDigest\" ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("CK_Enrollment_Lifetime",
                "\"ExpiresAt\" > \"IssuedAt\" AND \"ExpiresAt\" <= \"IssuedAt\" + interval '15 minutes'");
            table.HasCheckConstraint("CK_Enrollment_SigningAttempt", "NOT \"SigningAttempted\" OR \"OperationId\" IS NOT NULL");
            table.HasCheckConstraint("CK_Enrollment_Completion",
                "\"CompletedResponseJson\" IS NULL OR (\"OperationId\" IS NOT NULL AND " +
                "(jsonb_typeof(\"CompletedResponseJson\"::jsonb) = 'object' AND " +
                "\"CompletedResponseJson\"::jsonb ->> 'schemaVersion' = '1' AND " +
                "\"CompletedResponseJson\"::jsonb ->> 'deviceId' = \"DeviceId\"::text) IS TRUE)");
            table.HasCheckConstraint("CK_Enrollment_Claim",
                "(\"OperationId\" IS NULL AND \"DeviceId\" IS NULL AND \"CsrSha256\" IS NULL AND \"SubjectPublicKeyInfoBase64\" IS NULL AND \"ClaimedAt\" IS NULL) OR " +
                "(\"OperationId\" IS NOT NULL AND \"DeviceId\" IS NOT NULL AND \"CsrSha256\" IS NOT NULL AND \"SubjectPublicKeyInfoBase64\" IS NOT NULL AND \"ClaimedAt\" IS NOT NULL " +
                "AND \"OperationId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"DeviceId\" <> '00000000-0000-0000-0000-000000000000'::uuid " +
                "AND \"CsrSha256\" ~ '^[0-9a-f]{64}$' AND length(\"SubjectPublicKeyInfoBase64\") > 0 AND \"ClaimedAt\" >= \"IssuedAt\" AND \"ClaimedAt\" < \"ExpiresAt\")");
        });
        enrollment.HasKey(value => value.Id);
        enrollment.Property(value => value.Id).ValueGeneratedNever();
        enrollment.Property(value => value.TokenDigest).HasMaxLength(64).IsRequired();
        enrollment.Property(value => value.CsrSha256).HasMaxLength(64);
        enrollment.Property(value => value.SubjectPublicKeyInfoBase64).HasMaxLength(512);
        enrollment.Property(value => value.CompletedResponseJson).HasMaxLength(100000);
        enrollment.Property(value => value.SigningAttempted).HasDefaultValue(false);
        enrollment.Property(value => value.DeviceAccessDisabled).HasDefaultValue(false);
        enrollment.HasIndex(value => value.OperationId).IsUnique();
        enrollment.HasIndex(value => value.DeviceId).IsUnique();
        enrollment.Ignore(value => value.Credential);
        
        // Policy entities
        var policy = modelBuilder.Entity<Policy>();
        policy.ToTable("Policies");
        policy.HasKey(value => value.PolicyId);
        policy.Property(value => value.PolicyId).ValueGeneratedNever();
        policy.Property(value => value.Creator).HasMaxLength(256).IsRequired();
        policy.Property(value => value.PolicyJson).HasMaxLength(65536).IsRequired();
        policy.HasIndex(value => value.IsActive);
        policy.HasIndex(value => new { value.IsActive, value.ModifiedAt });

        var assignment = modelBuilder.Entity<PolicyAssignment>();
        assignment.ToTable("PolicyAssignments");
        assignment.HasKey(value => value.AssignmentId);
        assignment.Property(value => value.AssignmentId).ValueGeneratedNever();
        assignment.HasOne<PendingEnrollment>().WithMany().HasForeignKey(value => value.EnrollmentId)
            .OnDelete(DeleteBehavior.Restrict);
        assignment.HasOne<Policy>().WithMany().HasForeignKey(value => value.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);
        assignment.HasIndex(value => value.EnrollmentId);
        assignment.HasIndex(value => new { value.EnrollmentId, value.AssignedAt });

        // Release entities
        var release = modelBuilder.Entity<Release>();
        release.ToTable("Releases");
        release.HasKey(value => value.ReleaseId);
        release.Property(value => value.ReleaseId).ValueGeneratedNever();
        release.Property(value => value.PackageName).HasMaxLength(256).IsRequired();
        release.Property(value => value.VersionCode).IsRequired();
        release.Property(value => value.ApkSha256).HasMaxLength(64).IsRequired();
        release.Property(value => value.ApkSizeBytes).IsRequired();
        release.Property(value => value.MinSdkLevel).IsRequired();
        release.Property(value => value.ExpiresAt).IsRequired();
        release.Property(value => value.MetadataJson).HasMaxLength(65536).IsRequired();
        release.Property(value => value.MetadataSignature).HasMaxLength(256).IsRequired();
        release.Property(value => value.CreatedBy).HasMaxLength(256).IsRequired();
        release.HasIndex(value => value.IsActive);
        release.HasIndex(value => new { value.IsActive, value.CreatedAt });

        var deployment = modelBuilder.Entity<ReleaseDeployment>();
        deployment.ToTable("ReleaseDeployments");
        deployment.HasKey(value => value.DeploymentId);
        deployment.Property(value => value.DeploymentId).ValueGeneratedNever();
        deployment.Property(value => value.ReleaseId).IsRequired();
        deployment.Property(value => value.Stage).IsRequired();
        deployment.Property(value => value.RolloutPercentage).IsRequired();
        deployment.Property(value => value.CreatedBy).HasMaxLength(256).IsRequired();
        deployment.OwnsOne(value => value.Progress);
        deployment.HasOne<Release>().WithMany().HasForeignKey(value => value.ReleaseId)
            .OnDelete(DeleteBehavior.Restrict);
        deployment.HasIndex(value => value.IsActive);
        deployment.HasIndex(value => value.ReleaseId);

        var deviceState = modelBuilder.Entity<DeploymentDeviceState>();
        deviceState.ToTable("DeploymentDeviceStates");
        deviceState.HasKey(value => value.StateId);
        deviceState.Property(value => value.StateId).ValueGeneratedNever();
        deviceState.Property(value => value.DeploymentId).IsRequired();
        deviceState.Property(value => value.EnrollmentId).IsRequired();
        deviceState.Property(value => value.Status).IsRequired();
        deviceState.Property(value => value.ErrorMessage).HasMaxLength(512);
        deviceState.HasOne<ReleaseDeployment>().WithMany().HasForeignKey(value => value.DeploymentId)
            .OnDelete(DeleteBehavior.Restrict);
        deviceState.HasOne<PendingEnrollment>().WithMany().HasForeignKey(value => value.EnrollmentId)
            .OnDelete(DeleteBehavior.Restrict);
        deviceState.HasIndex(value => new { value.DeploymentId, value.EnrollmentId }).IsUnique();
        deviceState.HasIndex(value => value.Status);

        // Apply Device entity configurations
        modelBuilder.ApplyConfiguration(new DeviceConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceTagConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceTagAssignmentConfiguration());
        modelBuilder.ApplyConfiguration(new DesiredDeviceStateConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceReconciliationAuditLogConfiguration());
    }
}

public sealed class FleetDbContextDesignFactory : IDesignTimeDbContextFactory<FleetDbContext>
{
    public FleetDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<FleetDbContext>()
        .UseNpgsql("Host=localhost;Database=fleet_design_only;Username=unused").Options);
}