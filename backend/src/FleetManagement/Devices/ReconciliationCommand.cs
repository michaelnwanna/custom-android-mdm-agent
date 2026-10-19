using System;
using System.Collections.Generic;

namespace FleetManagement.Devices;

/// <summary>
/// Command generated as a result of device state reconciliation.
/// Specifies what action the device should take to reach desired state.
/// </summary>
public class ReconciliationCommand
{
    /// <summary>Unique command identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>ID of the device this command targets.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Enrollment ID associated with the device.</summary>
    public Guid EnrollmentId { get; set; }

    /// <summary>ID of the reconciliation audit log that triggered this command.</summary>
    public Guid? AuditLogId { get; set; }

    /// <summary>Type of reconciliation command.</summary>
    public ReconciliationCommandType CommandType { get; set; }

    /// <summary>Current status of command execution.</summary>
    public ReconciliationCommandStatus Status { get; set; }

    /// <summary>Priority level for command execution (0=highest, 100=lowest).</summary>
    public int Priority { get; set; }

    /// <summary>The specific resource ID affected by this command (policyId, releaseId, tagId, etc).</summary>
    public Guid? ResourceId { get; set; }

    /// <summary>Human-readable description of what this command does.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Detailed payload/parameters for command execution (JSON).</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>When this command was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When this command should expire (device should not execute after this time).</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>When the device should execute this command (or immediately if null).</summary>
    public DateTime? ScheduledFor { get; set; }

    /// <summary>Timestamp of last status update.</summary>
    public DateTime? LastStatusUpdate { get; set; }

    /// <summary>Number of delivery attempts made to the device.</summary>
    public int DeliveryAttempts { get; set; }

    /// <summary>Maximum number of times to attempt delivery.</summary>
    public int MaxDeliveryAttempts { get; set; } = 3;

    /// <summary>Result/response from device after execution (JSON).</summary>
    public string? ExecutionResultJson { get; set; }

    /// <summary>Error message if command execution failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>When this command was completed/executed.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Check if this command is expired.</summary>
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;

    /// <summary>Check if this command should be executed (scheduled time reached).</summary>
    public bool IsReady => !ScheduledFor.HasValue || ScheduledFor.Value <= DateTime.UtcNow;

    /// <summary>Check if delivery attempts have been exhausted.</summary>
    public bool IsAbandoned => DeliveryAttempts >= MaxDeliveryAttempts;

    /// <summary>Check if this command is still pending (not completed and not expired).</summary>
    public bool IsPending => Status == ReconciliationCommandStatus.Pending && !IsExpired && DeliveryAttempts < MaxDeliveryAttempts;
}

/// <summary>
/// Type of command generated during reconciliation.
/// </summary>
public enum ReconciliationCommandType
{
    /// <summary>Update device policy.</summary>
    AssignPolicy = 0,

    /// <summary>Install/update a release.</summary>
    AssignRelease = 1,

    /// <summary>Assign one or more tags.</summary>
    AssignTag = 2,

    /// <summary>Unassign a tag.</summary>
    UnassignTag = 3,

    /// <summary>Perform full state synchronization.</summary>
    SyncAllState = 4,

    /// <summary>Verify device state matches desired state.</summary>
    VerifyState = 5
}

/// <summary>
/// Status of a reconciliation command.
/// </summary>
public enum ReconciliationCommandStatus
{
    /// <summary>Command generated, awaiting delivery to device.</summary>
    Pending = 0,

    /// <summary>Command delivered to device, awaiting execution.</summary>
    Delivered = 1,

    /// <summary>Device reported command executed successfully.</summary>
    Executed = 2,

    /// <summary>Device reported command execution failed.</summary>
    Failed = 3,

    /// <summary>Command expired before device could execute.</summary>
    Expired = 4,

    /// <summary>Command manually cancelled.</summary>
    Cancelled = 5
}
