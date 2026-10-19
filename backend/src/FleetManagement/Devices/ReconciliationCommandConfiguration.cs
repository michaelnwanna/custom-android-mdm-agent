using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FleetManagement.Devices;

/// <summary>
/// EF Core configuration for ReconciliationCommand.
/// </summary>
public class ReconciliationCommandConfiguration : IEntityTypeConfiguration<ReconciliationCommand>
{
    public void Configure(EntityTypeBuilder<ReconciliationCommand> builder)
    {
        builder.ToTable("ReconciliationCommands");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.DeviceId)
            .IsRequired();

        builder.Property(x => x.EnrollmentId)
            .IsRequired();

        builder.Property(x => x.AuditLogId);

        builder.Property(x => x.CommandType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(x => x.Priority)
            .IsRequired()
            .HasDefaultValue(50);

        builder.Property(x => x.ResourceId);

        builder.Property(x => x.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.PayloadJson)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(x => x.ExpiresAt);

        builder.Property(x => x.ScheduledFor);

        builder.Property(x => x.LastStatusUpdate);

        builder.Property(x => x.DeliveryAttempts)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.MaxDeliveryAttempts)
            .IsRequired()
            .HasDefaultValue(3);

        builder.Property(x => x.ExecutionResultJson)
            .HasMaxLength(4000);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(x => x.CompletedAt);

        // Indexes for common queries
        builder.HasIndex(x => x.DeviceId)
            .HasDatabaseName("IX_ReconciliationCommands_DeviceId");

        builder.HasIndex(x => x.EnrollmentId)
            .HasDatabaseName("IX_ReconciliationCommands_EnrollmentId");

        builder.HasIndex(x => x.Status)
            .HasDatabaseName("IX_ReconciliationCommands_Status");

        builder.HasIndex(x => new { x.DeviceId, x.Status })
            .HasDatabaseName("IX_ReconciliationCommands_DeviceId_Status");

        builder.HasIndex(x => x.AuditLogId)
            .HasDatabaseName("IX_ReconciliationCommands_AuditLogId");

        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("IX_ReconciliationCommands_CreatedAt");

        builder.HasIndex(x => x.Priority)
            .HasDatabaseName("IX_ReconciliationCommands_Priority");

        builder.HasIndex(x => new { x.Status, x.Priority, x.ScheduledFor })
            .HasDatabaseName("IX_ReconciliationCommands_ReadyForDelivery");
    }
}
