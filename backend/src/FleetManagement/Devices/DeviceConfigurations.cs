namespace FleetManagement.Devices;

using FleetManagement.Enrollment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

/// <summary>
/// EF Core configuration for Device entity.
/// Establishes foreign key, indexes, and constraints.
/// </summary>
public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.HasKey(d => d.DeviceId);

        builder.Property(d => d.DeviceId)
            .HasColumnName("DeviceId")
            .ValueGeneratedNever();

        builder.Property(d => d.EnrollmentId)
            .IsRequired()
            .HasColumnName("EnrollmentId");

        builder.Property(d => d.DeviceType)
            .HasMaxLength(64);

        builder.Property(d => d.Manufacturer)
            .HasMaxLength(128);

        builder.Property(d => d.Model)
            .HasMaxLength(128);

        builder.Property(d => d.OsVersion);

        builder.Property(d => d.FirstEnrolledAt)
            .IsRequired();

        builder.Property(d => d.LastSeenAt)
            .IsRequired();

        builder.Property(d => d.State)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(d => d.AccessDisabled)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(d => d.CreatedAt)
            .IsRequired()
            .ValueGeneratedOnAdd();

        builder.Property(d => d.UpdatedAt)
            .IsRequired()
            .ValueGeneratedOnAddOrUpdate();

        // Foreign key to PendingEnrollment
        builder.HasOne<PendingEnrollment>()
            .WithMany()
            .HasForeignKey(d => d.EnrollmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes for common queries
        builder.HasIndex(d => d.EnrollmentId)
            .HasDatabaseName("IX_Devices_EnrollmentId");

        builder.HasIndex(d => d.State)
            .HasDatabaseName("IX_Devices_State");

        builder.HasIndex(d => d.LastSeenAt)
            .HasDatabaseName("IX_Devices_LastSeenAt");

        builder.HasIndex(d => d.AccessDisabled)
            .HasDatabaseName("IX_Devices_AccessDisabled");

        builder.ToTable("Devices");
    }
}

/// <summary>
/// EF Core configuration for DeviceTag entity.
/// Establishes unique constraint on tag name.
/// </summary>
public class DeviceTagConfiguration : IEntityTypeConfiguration<DeviceTag>
{
    public void Configure(EntityTypeBuilder<DeviceTag> builder)
    {
        builder.HasKey(t => t.TagId);

        builder.Property(t => t.TagId)
            .HasColumnName("TagId")
            .ValueGeneratedNever();

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("Name");

        builder.Property(t => t.Description)
            .HasMaxLength(512);

        builder.Property(t => t.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(t => t.CreatedBy)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(t => t.CreatedAt)
            .IsRequired()
            .ValueGeneratedOnAdd();

        builder.Property(t => t.UpdatedAt)
            .IsRequired()
            .ValueGeneratedOnAddOrUpdate();

        // Unique constraint on normalized tag name
        builder.HasIndex(t => t.Name)
            .IsUnique()
            .HasDatabaseName("IX_DeviceTags_Name_Unique");

        // Index for active tags queries
        builder.HasIndex(t => t.IsActive)
            .HasDatabaseName("IX_DeviceTags_IsActive");

        builder.ToTable("DeviceTags");
    }
}

/// <summary>
/// EF Core configuration for DeviceTagAssignment entity.
/// Establishes foreign keys and unique constraint on active assignments.
/// </summary>
public class DeviceTagAssignmentConfiguration : IEntityTypeConfiguration<DeviceTagAssignment>
{
    public void Configure(EntityTypeBuilder<DeviceTagAssignment> builder)
    {
        builder.HasKey(a => a.AssignmentId);

        builder.Property(a => a.AssignmentId)
            .HasColumnName("AssignmentId")
            .ValueGeneratedNever();

        builder.Property(a => a.DeviceId)
            .IsRequired()
            .HasColumnName("DeviceId");

        builder.Property(a => a.TagId)
            .IsRequired()
            .HasColumnName("TagId");

        builder.Property(a => a.AssignedAt)
            .IsRequired();

        builder.Property(a => a.AssignedBy)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(a => a.RemovedAt);

        builder.Property(a => a.RemovedBy)
            .HasMaxLength(256);

        // Foreign keys
        builder.HasOne(a => a.Device)
            .WithMany(d => d.TagAssignments)
            .HasForeignKey(a => a.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Tag)
            .WithMany(t => t.Assignments)
            .HasForeignKey(a => a.TagId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique constraint: only one active assignment per (DeviceId, TagId)
        // We use a partial unique index on (DeviceId, TagId) where RemovedAt IS NULL
        builder.HasIndex(a => new { a.DeviceId, a.TagId, a.RemovedAt })
            .IsUnique()
            .HasDatabaseName("IX_DeviceTagAssignments_Device_Tag_Active");

        // Indexes for common queries
        builder.HasIndex(a => a.DeviceId)
            .HasDatabaseName("IX_DeviceTagAssignments_DeviceId");

        builder.HasIndex(a => a.TagId)
            .HasDatabaseName("IX_DeviceTagAssignments_TagId");

        builder.HasIndex(a => a.RemovedAt)
            .HasDatabaseName("IX_DeviceTagAssignments_RemovedAt");

        builder.ToTable("DeviceTagAssignments");
    }
}
