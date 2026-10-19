namespace FleetManagement.Devices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

/// <summary>
/// EF Core configuration for DeviceTelemetry and DeviceHealthAlert entities.
/// </summary>
public class TelemetryConfiguration : IEntityTypeConfiguration<DeviceTelemetry>, IEntityTypeConfiguration<DeviceHealthAlert>
{
    /// <summary>
    /// Configure the DeviceTelemetry entity.
    /// </summary>
    public void Configure(EntityTypeBuilder<DeviceTelemetry> builder)
    {
        // Table and primary key
        builder.ToTable("device_telemetry");
        builder.HasKey(t => t.TelemetryId);

        // Properties
        builder.Property(t => t.TelemetryId).HasColumnName("telemetry_id");
        builder.Property(t => t.DeviceId).HasColumnName("device_id");
        builder.Property(t => t.BatteryLevel).HasColumnName("battery_level");
        builder.Property(t => t.IsCharging).HasColumnName("is_charging");
        builder.Property(t => t.StorageUsedMb).HasColumnName("storage_used_mb");
        builder.Property(t => t.StorageTotalMb).HasColumnName("storage_total_mb");
        builder.Property(t => t.StorageFreeMb).HasColumnName("storage_free_mb");
        builder.Property(t => t.AvailableMemoryMb).HasColumnName("available_memory_mb");
        builder.Property(t => t.TotalMemoryMb).HasColumnName("total_memory_mb");
        builder.Property(t => t.Connectivity).HasColumnName("connectivity");
        builder.Property(t => t.WifiSignalStrengthDbm).HasColumnName("wifi_signal_strength_dbm");
        builder.Property(t => t.CellularSignalStrengthDbm).HasColumnName("cellular_signal_strength_dbm");
        builder.Property(t => t.CpuUsagePercent).HasColumnName("cpu_usage_percent");
        builder.Property(t => t.LastCheckInAt).HasColumnName("last_check_in_at");
        builder.Property(t => t.SnapshotTakenAt).HasColumnName("snapshot_taken_at");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");

        // Foreign key to Device
        builder.HasOne(t => t.Device)
            .WithMany()
            .HasForeignKey(t => t.DeviceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_device_telemetry_device");

        // Indexes for efficient querying
        // Query by device and creation time (most common: latest telemetry for a device)
        builder.HasIndex(t => new { t.DeviceId, t.CreatedAt })
            .HasDatabaseName("ix_device_telemetry_device_created");

        // Query by device and snapshot time (for historical analysis)
        builder.HasIndex(t => new { t.DeviceId, t.SnapshotTakenAt })
            .HasDatabaseName("ix_device_telemetry_device_snapshot");

        // Query for cleanup: all telemetry older than X days
        builder.HasIndex(t => t.CreatedAt)
            .HasDatabaseName("ix_device_telemetry_created");

        // Query for alert triggering: low battery, high memory, etc. (useful for scanning)
        builder.HasIndex(t => new { t.DeviceId, t.BatteryLevel })
            .HasDatabaseName("ix_device_telemetry_battery");

        builder.HasIndex(t => new { t.DeviceId, t.Connectivity })
            .HasDatabaseName("ix_device_telemetry_connectivity");
    }

    /// <summary>
    /// Configure the DeviceHealthAlert entity.
    /// </summary>
    public void Configure(EntityTypeBuilder<DeviceHealthAlert> builder)
    {
        // Table and primary key
        builder.ToTable("device_health_alerts");
        builder.HasKey(a => a.AlertId);

        // Properties
        builder.Property(a => a.AlertId).HasColumnName("alert_id");
        builder.Property(a => a.DeviceId).HasColumnName("device_id");
        builder.Property(a => a.AlertType).HasColumnName("alert_type");
        builder.Property(a => a.Severity).HasColumnName("severity");
        builder.Property(a => a.Message).HasColumnName("message").HasMaxLength(500);
        builder.Property(a => a.TelemetryId).HasColumnName("telemetry_id");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(a => a.ResolutionReason).HasColumnName("resolution_reason").HasMaxLength(500);

        // Foreign key to Device
        builder.HasOne(a => a.Device)
            .WithMany()
            .HasForeignKey(a => a.DeviceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_device_health_alerts_device");

        // Foreign key to DeviceTelemetry
        builder.HasOne(a => a.Telemetry)
            .WithMany()
            .HasForeignKey(a => a.TelemetryId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_device_health_alerts_telemetry");

        // Indexes for efficient querying
        // Query active alerts for a device (ResolvedAt is null)
        builder.HasIndex(a => new { a.DeviceId, a.ResolvedAt })
            .HasDatabaseName("ix_device_health_alerts_device_active");

        // Query alerts by severity (for dashboard "critical alerts" view)
        builder.HasIndex(a => new { a.Severity, a.ResolvedAt })
            .HasDatabaseName("ix_device_health_alerts_severity_active");

        // Query alerts by type (for grouping by issue category)
        builder.HasIndex(a => new { a.AlertType, a.ResolvedAt })
            .HasDatabaseName("ix_device_health_alerts_type_active");

        // Query for cleanup: resolved alerts older than X days
        builder.HasIndex(a => a.ResolvedAt)
            .HasDatabaseName("ix_device_health_alerts_resolved");

        // Query all alerts for a device including resolved (for history)
        builder.HasIndex(a => new { a.DeviceId, a.CreatedAt })
            .HasDatabaseName("ix_device_health_alerts_device_created");

        // Query alerts by creation time (for time-range queries)
        builder.HasIndex(a => a.CreatedAt)
            .HasDatabaseName("ix_device_health_alerts_created");
    }
}
