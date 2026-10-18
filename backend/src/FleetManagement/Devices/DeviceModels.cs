namespace FleetManagement.Devices;

using System;

/// <summary>
/// Represents a managed device in the fleet.
/// A device is created when enrollment is claimed and provides durable device identity
/// independent of the enrollment history.
/// </summary>
public class Device
{
    /// <summary>
    /// Unique device identifier (GUID, generated at claim time).
    /// Immutable; used for all device communications after enrollment.
    /// </summary>
    public Guid DeviceId { get; private set; }

    /// <summary>
    /// Reference to the enrollment record for this device.
    /// Allows tracking enrollment history and CSR/certificate details.
    /// </summary>
    public Guid EnrollmentId { get; private set; }

    /// <summary>
    /// Device type (optional). Examples: phone, tablet, kiosk.
    /// Can be populated from device profile or enrollment data.
    /// </summary>
    public string? DeviceType { get; private set; }

    /// <summary>
    /// Manufacturer name (optional). Examples: Samsung, Google, Apple.
    /// Populated from device profile if available.
    /// </summary>
    public string? Manufacturer { get; private set; }

    /// <summary>
    /// Device model (optional). Examples: Pixel 6, Galaxy S21.
    /// Populated from device profile if available.
    /// </summary>
    public string? Model { get; private set; }

    /// <summary>
    /// Android OS version (optional). Examples: 13, 14.
    /// Populated from device profile if available.
    /// </summary>
    public int? OsVersion { get; private set; }

    /// <summary>
    /// Timestamp when device enrollment was completed.
    /// Immutable once set.
    /// </summary>
    public DateTimeOffset FirstEnrolledAt { get; private set; }

    /// <summary>
    /// Timestamp of the last communication from this device.
    /// Updated on any MQTT message, status report, or API call.
    /// </summary>
    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>
    /// Current device state. Examples: Active, Retired, Lost.
    /// Immutable until changed via explicit state transition.
    /// </summary>
    public DeviceState State { get; private set; }

    /// <summary>
    /// Whether device admin access is disabled.
    /// Prevents device from executing admin commands.
    /// Can be set to lock down or decommission a device.
    /// </summary>
    public bool AccessDisabled { get; private set; }

    /// <summary>
    /// Record creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Last modification timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    // Navigation properties
    /// <summary>
    /// Tags assigned to this device.
    /// </summary>
    public ICollection<DeviceTagAssignment> TagAssignments { get; private set; } = new List<DeviceTagAssignment>();

    private Device() { }

    /// <summary>
    /// Create a new managed device from enrollment.
    /// Called during enrollment claim when DeviceId is generated.
    /// </summary>
    public static Device Create(Guid deviceId, Guid enrollmentId, DeviceState initialState = DeviceState.Active)
    {
        var now = DateTimeOffset.UtcNow;
        return new Device
        {
            DeviceId = deviceId,
            EnrollmentId = enrollmentId,
            State = initialState,
            AccessDisabled = false,
            FirstEnrolledAt = now,
            LastSeenAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Update the last-seen timestamp on device activity.
    /// Called from status reports, MQTT messages, or API calls.
    /// </summary>
    public void UpdateLastSeen()
    {
        LastSeenAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Update device profile information from device metadata.
    /// </summary>
    public void UpdateProfile(string? deviceType, string? manufacturer, string? model, int? osVersion)
    {
        DeviceType = deviceType;
        Manufacturer = manufacturer;
        Model = model;
        OsVersion = osVersion;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Set or clear the access-disabled flag.
    /// </summary>
    public void SetAccessDisabled(bool disabled)
    {
        AccessDisabled = disabled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Transition device to a new state.
    /// </summary>
    public void TransitionToState(DeviceState newState)
    {
        State = newState;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Device lifecycle state.
/// </summary>
public enum DeviceState
{
    /// <summary>Device is active and receiving deployments.</summary>
    Active = 0,

    /// <summary>Device is retired and no longer targeted for deployments.</summary>
    Retired = 1,

    /// <summary>Device is marked as lost and should be monitored.</summary>
    Lost = 2,

    /// <summary>Device is being replaced or decommissioned.</summary>
    Decommissioned = 3
}

/// <summary>
/// Represents a classification tag for devices.
/// Tags enable bulk operations by grouping devices with common characteristics.
/// Examples: prod, beta, region-us, pilot, flagship.
/// </summary>
public class DeviceTag
{
    /// <summary>
    /// Unique tag identifier.
    /// </summary>
    public Guid TagId { get; private set; }

    /// <summary>
    /// Normalized tag name (lowercase, trimmed, 1-64 chars).
    /// Unique constraint ensures one canonical tag per name.
    /// Examples: prod, beta, region-us, os-14.
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// Optional description of this tag's purpose.
    /// Examples: "Production fleet", "Beta testing devices in US".
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Whether this tag is actively used.
    /// False for deprecated tags; existing assignments may remain.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Subject who created this tag.
    /// </summary>
    public string CreatedBy { get; private set; } = null!;

    /// <summary>
    /// Tag creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Last modification timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    // Navigation properties
    /// <summary>
    /// Devices assigned this tag.
    /// </summary>
    public ICollection<DeviceTagAssignment> Assignments { get; private set; } = new List<DeviceTagAssignment>();

    private DeviceTag() { }

    /// <summary>
    /// Create a new device tag.
    /// Name is automatically normalized to lowercase and trimmed.
    /// </summary>
    public static DeviceTag Create(string name, string createdBy, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tag name cannot be empty", nameof(name));

        var normalizedName = name.Trim().ToLowerInvariant();
        if (normalizedName.Length > 64)
            throw new ArgumentException("Tag name cannot exceed 64 characters", nameof(name));

        var now = DateTimeOffset.UtcNow;
        return new DeviceTag
        {
            TagId = Guid.NewGuid(),
            Name = normalizedName,
            Description = description,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Update tag description and active status.
    /// </summary>
    public void Update(string? description, bool isActive)
    {
        Description = description;
        IsActive = isActive;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Represents the assignment of a tag to a device.
/// Supports soft-delete through RemovedAt to maintain audit trail.
/// </summary>
public class DeviceTagAssignment
{
    /// <summary>
    /// Unique assignment identifier.
    /// </summary>
    public Guid AssignmentId { get; private set; }

    /// <summary>
    /// Device being tagged.
    /// </summary>
    public Guid DeviceId { get; private set; }

    /// <summary>
    /// Tag applied to the device.
    /// </summary>
    public Guid TagId { get; private set; }

    /// <summary>
    /// When this tag was assigned to the device.
    /// </summary>
    public DateTimeOffset AssignedAt { get; private set; }

    /// <summary>
    /// Subject who assigned this tag.
    /// </summary>
    public string AssignedBy { get; private set; } = null!;

    /// <summary>
    /// When this tag was removed from the device (null if still active).
    /// Soft-delete allows audit trail.
    /// </summary>
    public DateTimeOffset? RemovedAt { get; private set; }

    /// <summary>
    /// Subject who removed this tag (null if still active).
    /// </summary>
    public string? RemovedBy { get; private set; }

    // Navigation properties
    /// <summary>
    /// The device for this assignment.
    /// </summary>
    public Device Device { get; private set; } = null!;

    /// <summary>
    /// The tag for this assignment.
    /// </summary>
    public DeviceTag Tag { get; private set; } = null!;

    private DeviceTagAssignment() { }

    /// <summary>
    /// Create a new tag assignment.
    /// </summary>
    public static DeviceTagAssignment Create(Guid deviceId, Guid tagId, string assignedBy)
    {
        if (deviceId == Guid.Empty)
            throw new ArgumentException("DeviceId cannot be empty", nameof(deviceId));
        if (tagId == Guid.Empty)
            throw new ArgumentException("TagId cannot be empty", nameof(tagId));
        if (string.IsNullOrWhiteSpace(assignedBy))
            throw new ArgumentException("AssignedBy cannot be empty", nameof(assignedBy));

        return new DeviceTagAssignment
        {
            AssignmentId = Guid.NewGuid(),
            DeviceId = deviceId,
            TagId = tagId,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedBy = assignedBy,
            RemovedAt = null,
            RemovedBy = null
        };
    }

    /// <summary>
    /// Mark this assignment as removed (soft-delete).
    /// </summary>
    public void Remove(string removedBy)
    {
        if (RemovedAt.HasValue)
            throw new InvalidOperationException("This tag assignment has already been removed");

        RemovedAt = DateTimeOffset.UtcNow;
        RemovedBy = removedBy;
    }

    /// <summary>
    /// Check if this assignment is currently active (not removed).
    /// </summary>
    public bool IsActive => RemovedAt == null;
}
