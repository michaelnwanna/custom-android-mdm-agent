using FleetManagement.Enrollment;
using FleetManagement.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FleetManagement.Persistence;

public sealed class FleetDbContext(DbContextOptions<FleetDbContext> options) : DbContext(options)
{
    public DbSet<PendingEnrollment> PendingEnrollments => Set<PendingEnrollment>();
    public DbSet<EnrollmentProvisioningAudit> EnrollmentProvisioningAudits => Set<EnrollmentProvisioningAudit>();
    public DbSet<CommandEventOwnership> CommandEventOwnerships => Set<CommandEventOwnership>();
    public DbSet<StoredCommandEvent> StoredCommandEvents => Set<StoredCommandEvent>();

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
    }
}

public sealed class FleetDbContextDesignFactory : IDesignTimeDbContextFactory<FleetDbContext>
{
    public FleetDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<FleetDbContext>()
        .UseNpgsql("Host=localhost;Database=fleet_design_only;Username=unused").Options);
}