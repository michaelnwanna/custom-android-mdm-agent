using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FleetManagement.Devices;

/// <summary>
/// EF Core configuration for DeviceReconciliationAuditLog.
/// </summary>
public class DeviceReconciliationAuditLogConfiguration : IEntityTypeConfiguration<DeviceReconciliationAuditLog>
{
    public void Configure(EntityTypeBuilder<DeviceReconciliationAuditLog> builder)
    {
        builder.ToTable("DeviceReconciliationAuditLogs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.DeviceId)
            .IsRequired();

        builder.Property(x => x.EnrollmentId)
            .IsRequired();

        builder.Property(x => x.ReconciliationTimestamp)
            .IsRequired();

        builder.Property(x => x.ReconciliationType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(x => x.ReconciliationStatus)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(x => x.DesiredPolicyIdsString)
            .IsRequired()
            .HasMaxLength(2000)
            .HasColumnName("DesiredPolicyIds");

        builder.Property(x => x.ObservedPolicyIdsString)
            .IsRequired()
            .HasMaxLength(2000)
            .HasColumnName("ObservedPolicyIds");

        builder.Property(x => x.DesiredReleaseId);

        builder.Property(x => x.ObservedReleaseId);

        builder.Property(x => x.DesiredTagIdsString)
            .IsRequired()
            .HasMaxLength(2000)
            .HasColumnName("DesiredTagIds");

        builder.Property(x => x.ObservedTagIdsString)
            .IsRequired()
            .HasMaxLength(2000)
            .HasColumnName("ObservedTagIds");

        builder.Property(x => x.ConflictTypesString)
            .IsRequired()
            .HasMaxLength(500)
            .HasColumnName("ConflictTypes");

        builder.Property(x => x.ConflictDetails)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(x => x.DurationMilliseconds)
            .IsRequired();

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(x => x.GeneratedCommandIdsString)
            .IsRequired()
            .HasMaxLength(2000)
            .HasColumnName("GeneratedCommandIds");

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // Indexes for common queries
        builder.HasIndex(x => x.DeviceId)
            .HasDatabaseName("IX_DeviceReconciliationAuditLogs_DeviceId");

        builder.HasIndex(x => x.EnrollmentId)
            .HasDatabaseName("IX_DeviceReconciliationAuditLogs_EnrollmentId");

        builder.HasIndex(x => x.ReconciliationTimestamp)
            .HasDatabaseName("IX_DeviceReconciliationAuditLogs_ReconciliationTimestamp");

        builder.HasIndex(x => new { x.DeviceId, x.ReconciliationTimestamp })
            .HasDatabaseName("IX_DeviceReconciliationAuditLogs_DeviceId_Timestamp");

        builder.HasIndex(x => x.ReconciliationStatus)
            .HasDatabaseName("IX_DeviceReconciliationAuditLogs_Status");

        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("IX_DeviceReconciliationAuditLogs_CreatedAt");
    }
}
