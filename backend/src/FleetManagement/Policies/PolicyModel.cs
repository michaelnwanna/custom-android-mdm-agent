namespace FleetManagement.Policies;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// A policy represents desired device configuration (restrictions, app allowlist, etc).
/// Policies are versioned and immutable once created; updates produce new policy IDs.
/// </summary>
[Table("Policies")]
public sealed class Policy
{
    /// <summary>
    /// Immutable policy ID, derived from the JSON content hash.
    /// Same JSON always produces the same ID (idempotent creation).
    /// </summary>
    [Key]
    public Guid PolicyId { get; set; }

    /// <summary>
    /// The subject (administrator) who created this policy.
    /// </summary>
    [Required]
    [StringLength(256)]
    public required string Creator { get; set; }

    /// <summary>
    /// Current version number, incremented on updates.
    /// Devices receive only the latest active version.
    /// </summary>
    public long CurrentVersion { get; set; } = 1;

    /// <summary>
    /// Compact JSON representation of the policy (max 64 KB).
    /// No parsing on device; loaded once and persisted as-is.
    /// </summary>
    [Required]
    [StringLength(65536)]
    public required string PolicyJson { get; set; }

    /// <summary>
    /// UTC timestamp when this policy was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// UTC timestamp when this policy was last modified.
    /// </summary>
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// True if this policy can be assigned to new devices.
    /// When false, the policy is archived; existing assignments remain valid.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Records the assignment of a policy to a device.
/// Tracks which policy version was assigned and when the device acknowledged it.
/// </summary>
[Table("PolicyAssignments")]
public sealed class PolicyAssignment
{
    /// <summary>
    /// Unique assignment ID.
    /// </summary>
    [Key]
    public Guid AssignmentId { get; set; }

    /// <summary>
    /// Device receiving this policy (foreign key to PendingEnrollment.EnrollmentId).
    /// </summary>
    [ForeignKey(nameof(EnrollmentId))]
    public Guid EnrollmentId { get; set; }

    /// <summary>
    /// Policy being assigned (foreign key).
    /// </summary>
    [ForeignKey(nameof(PolicyId))]
    public Guid PolicyId { get; set; }

    /// <summary>
    /// Navigation property to the policy (for eager loading).
    /// </summary>
    public Policy? Policy { get; set; }

    /// <summary>
    /// Policy version at the time of assignment.
    /// Commands will include this version so devices can detect updates.
    /// </summary>
    public long AssignedVersion { get; set; }

    /// <summary>
    /// The highest policy version the device has reported receiving.
    /// Null until the device acknowledges via a command event.
    /// </summary>
    public long? AcknowledgedVersion { get; set; }

    /// <summary>
    /// UTC timestamp when this assignment was created.
    /// </summary>
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// UTC timestamp when the device last reported on this assignment.
    /// Used to track delivery freshness.
    /// </summary>
    public DateTimeOffset? LastReportedAt { get; set; }
}

/// <summary>
/// Device-side representation of policy state.
/// Persisted in Room; tracks current and acknowledged versions.
/// </summary>
public sealed class DevicePolicyState
{
    /// <summary>
    /// The policy ID currently assigned to this device.
    /// </summary>
    public Guid CurrentPolicyId { get; set; }

    /// <summary>
    /// The version of the currently assigned policy.
    /// </summary>
    public long AssignedVersion { get; set; }

    /// <summary>
    /// The highest version the device has successfully applied.
    /// </summary>
    public long AcknowledgedVersion { get; set; }

    /// <summary>
    /// The policy JSON (retained for offline application).
    /// </summary>
    public required string PolicyJson { get; set; }

    /// <summary>
    /// Timestamp of last update (device time, monotonic).
    /// </summary>
    public long LastUpdatedMs { get; set; }
}
