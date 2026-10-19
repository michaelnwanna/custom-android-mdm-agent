namespace FleetManagement.Devices;

using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

/// <summary>
/// EF Core configuration for DesiredDeviceState entity.
/// Stores authoritative device state for reconciliation.
/// </summary>
public class DesiredDeviceStateConfiguration : IEntityTypeConfiguration<DesiredDeviceState>
{
    public void Configure(EntityTypeBuilder<DesiredDeviceState> builder)
    {
        builder.ToTable("DesiredDeviceStates");

        // Primary key
        builder.HasKey(d => d.DeviceId);

        // Properties
        builder.Property(d => d.DeviceId)
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(d => d.CurrentPolicyId)
            .IsRequired(false);

        builder.Property(d => d.CurrentPolicyVersion)
            .IsRequired(false);

        // Store release and tag IDs as delimited strings
        builder.Property(d => d.DesiredReleaseIdsString)
            .HasColumnName("DesiredReleaseIds")
            .HasMaxLength(8000)
            .IsRequired()
            .HasDefaultValue(string.Empty);

        builder.Property(d => d.AssignedTagIdsString)
            .HasColumnName("AssignedTagIds")
            .HasMaxLength(8000)
            .IsRequired()
            .HasDefaultValue(string.Empty);

        builder.Property(d => d.ComputedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(d => d.ComputationHash)
            .IsRequired()
            .HasMaxLength(64); // SHA-256 hex = 64 chars

        builder.Property(d => d.IsValid)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(d => d.ExpiresAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        // Indexes for common queries
        builder.HasIndex(d => d.ComputedAt)
            .HasDatabaseName("IX_DesiredDeviceStates_ComputedAt");

        builder.HasIndex(d => d.ExpiresAt)
            .HasDatabaseName("IX_DesiredDeviceStates_ExpiresAt");

        builder.HasIndex(d => d.IsValid)
            .HasDatabaseName("IX_DesiredDeviceStates_IsValid");
    }
}
