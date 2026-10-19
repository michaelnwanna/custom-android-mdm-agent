using System;
using System.Collections.Generic;

namespace FleetManagement.Devices;

/// <summary>
/// Audit log for device state reconciliation events.
/// Tracks all reconciliation attempts, conflicts, and state changes for compliance and debugging.
/// </summary>
public class DeviceReconciliationAuditLog
{
    /// <summary>
    /// Unique identifier for this audit log entry.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// ID of the device being reconciled.
    /// </summary>
    public Guid DeviceId { get; set; }

    /// <summary>
    /// Enrollment ID associated with the device.
    /// </summary>
    public Guid EnrollmentId { get; set; }

    /// <summary>
    /// Timestamp when reconciliation was performed.
    /// </summary>
    public DateTime ReconciliationTimestamp { get; set; }

    /// <summary>
    /// Type of reconciliation event (e.g., Scheduled, Triggered, Reconnection).
    /// </summary>
    public ReconciliationType ReconciliationType { get; set; }

    /// <summary>
    /// Overall result of reconciliation (InSync, OutOfSync, ConflictsDetected, Error).
    /// </summary>
    public ReconciliationStatus ReconciliationStatus { get; set; }

    /// <summary>
    /// Desired policy IDs at time of reconciliation (semicolon-delimited).
    /// </summary>
    public string DesiredPolicyIdsString { get; set; } = string.Empty;

    /// <summary>
    /// Observed policy IDs reported by device (semicolon-delimited).
    /// </summary>
    public string ObservedPolicyIdsString { get; set; } = string.Empty;

    /// <summary>
    /// Desired release ID at time of reconciliation.
    /// </summary>
    public Guid? DesiredReleaseId { get; set; }

    /// <summary>
    /// Observed release ID reported by device.
    /// </summary>
    public Guid? ObservedReleaseId { get; set; }

    /// <summary>
    /// Desired tag IDs at time of reconciliation (semicolon-delimited).
    /// </summary>
    public string DesiredTagIdsString { get; set; } = string.Empty;

    /// <summary>
    /// Observed tag IDs reported by device (semicolon-delimited).
    /// </summary>
    public string ObservedTagIdsString { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated list of detected conflicts (e.g., "PolicyMismatch,ReleaseOutOfDate").
    /// </summary>
    public string ConflictTypesString { get; set; } = string.Empty;

    /// <summary>
    /// Detailed summary of conflicts and mismatches.
    /// </summary>
    public string ConflictDetails { get; set; } = string.Empty;

    /// <summary>
    /// Duration in milliseconds that reconciliation took.
    /// </summary>
    public long DurationMilliseconds { get; set; }

    /// <summary>
    /// Error message if reconciliation failed (null if successful).
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Semicolon-delimited list of commands generated as a result of reconciliation.
    /// </summary>
    public string GeneratedCommandIdsString { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when this audit log entry was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Helper to convert DesiredPolicyIdsString to list of GUIDs.
    /// </summary>
    public List<Guid> GetDesiredPolicyIds()
    {
        if (string.IsNullOrEmpty(DesiredPolicyIdsString))
            return new List<Guid>();

        return DesiredPolicyIdsString.Split(';', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(Guid.Parse)
            .ToList();
    }

    /// <summary>
    /// Helper to convert ObservedPolicyIdsString to list of GUIDs.
    /// </summary>
    public List<Guid> GetObservedPolicyIds()
    {
        if (string.IsNullOrEmpty(ObservedPolicyIdsString))
            return new List<Guid>();

        return ObservedPolicyIdsString.Split(';', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(Guid.Parse)
            .ToList();
    }

    /// <summary>
    /// Helper to convert DesiredTagIdsString to list of GUIDs.
    /// </summary>
    public List<Guid> GetDesiredTagIds()
    {
        if (string.IsNullOrEmpty(DesiredTagIdsString))
            return new List<Guid>();

        return DesiredTagIdsString.Split(';', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(Guid.Parse)
            .ToList();
    }

    /// <summary>
    /// Helper to convert ObservedTagIdsString to list of GUIDs.
    /// </summary>
    public List<Guid> GetObservedTagIds()
    {
        if (string.IsNullOrEmpty(ObservedTagIdsString))
            return new List<Guid>();

        return ObservedTagIdsString.Split(';', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(Guid.Parse)
            .ToList();
    }

    /// <summary>
    /// Helper to convert ConflictTypesString to list of conflict types.
    /// </summary>
    public List<StateChangeType> GetConflictTypes()
    {
        if (string.IsNullOrEmpty(ConflictTypesString))
            return new List<StateChangeType>();

        var types = new List<StateChangeType>();
        foreach (var typeStr in ConflictTypesString.Split(',', System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (System.Enum.TryParse<StateChangeType>(typeStr, out var type))
            {
                types.Add(type);
            }
        }
        return types;
    }

    /// <summary>
    /// Helper to convert GeneratedCommandIdsString to list of command IDs.
    /// </summary>
    public List<Guid> GetGeneratedCommandIds()
    {
        if (string.IsNullOrEmpty(GeneratedCommandIdsString))
            return new List<Guid>();

        return GeneratedCommandIdsString.Split(';', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(Guid.Parse)
            .ToList();
    }
}

/// <summary>
/// Type of reconciliation event.
/// </summary>
public enum ReconciliationType
{
    /// <summary>Scheduled periodic reconciliation.</summary>
    Scheduled,

    /// <summary>Reconciliation triggered by assignment change.</summary>
    TriggeredByAssignment,

    /// <summary>Reconciliation triggered by device reconnection after offline period.</summary>
    Reconnection,

    /// <summary>Manual reconciliation requested via API.</summary>
    Manual
}

/// <summary>
/// Result of device reconciliation.
/// </summary>
public enum ReconciliationStatus
{
    /// <summary>Device state matches desired state.</summary>
    InSync,

    /// <summary>Device state differs from desired state.</summary>
    OutOfSync,

    /// <summary>Conflicts detected that require manual intervention.</summary>
    ConflictsDetected,

    /// <summary>Reconciliation failed with an error.</summary>
    Error
}
