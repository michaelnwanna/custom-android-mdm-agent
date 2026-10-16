using FleetManagement.Enrollment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FleetManagement.Persistence;

public sealed class FleetDbContext(DbContextOptions<FleetDbContext> options) : DbContext(options)
{
    public DbSet<PendingEnrollment> PendingEnrollments => Set<PendingEnrollment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var enrollment = modelBuilder.Entity<PendingEnrollment>();
        enrollment.ToTable("PendingEnrollments", table =>
        {
            table.HasCheckConstraint("CK_Enrollment_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_Enrollment_Digest", "\"TokenDigest\" ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("CK_Enrollment_Lifetime",
                "\"ExpiresAt\" > \"IssuedAt\" AND \"ExpiresAt\" <= \"IssuedAt\" + interval '15 minutes'");
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