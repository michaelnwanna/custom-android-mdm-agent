namespace FleetManagement.Devices;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json.Serialization;

/// <summary>
/// Represents the authoritative desired state for a device.
/// Aggregates current policy, release assignments, and tags.
/// Used for reconciliation when device reconnects.
/// </summary>
public class DesiredDeviceState
{
    /// <summary>Device ID this state describes (immutable).</summary>
    [JsonPropertyName("deviceId")]
    public Guid DeviceId { get; private set; }

    /// <summary>Current policy ID (null if no policy assigned).</summary>
    [JsonPropertyName("currentPolicyId")]
    public Guid? CurrentPolicyId { get; set; }

    /// <summary>Current policy version (only valid if CurrentPolicyId is set).</summary>
    [JsonPropertyName("currentPolicyVersion")]
    public long? CurrentPolicyVersion { get; set; }

    /// <summary>List of assigned release IDs as semicolon-delimited string.</summary>
    [JsonIgnore]
    public string DesiredReleaseIdsString { get; set; } = string.Empty;

    /// <summary>Assigned device tag IDs as semicolon-delimited string.</summary>
    [JsonIgnore]
    public string AssignedTagIdsString { get; set; } = string.Empty;

    /// <summary>When this state was last computed from assignments.</summary>
    [JsonPropertyName("computedAt")]
    public DateTimeOffset ComputedAt { get; set; }

    /// <summary>SHA-256 hash of (CurrentPolicyId + CurrentPolicyVersion + ReleaseIds + TagIds) for optimization.</summary>
    [JsonPropertyName("computationHash")]
    public string ComputationHash { get; set; } = string.Empty;

    /// <summary>True if this state has been validated against database.</summary>
    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    /// <summary>TTL for this state (how long before recompute needed).</summary>
    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Get desired release IDs as list.
    /// </summary>
    [JsonPropertyName("desiredReleaseIds")]
    [NotMapped]
    public List<Guid> DesiredReleaseIds
    {
        get => ParseIds(DesiredReleaseIdsString);
        set => DesiredReleaseIdsString = FormatIds(value);
    }

    /// <summary>
    /// Get assigned tag IDs as list.
    /// </summary>
    [JsonPropertyName("assignedTagIds")]
    [NotMapped]
    public List<Guid> AssignedTagIds
    {
        get => ParseIds(AssignedTagIdsString);
        set => AssignedTagIdsString = FormatIds(value);
    }

    /// <summary>
    /// Factory: Create new desired state for a device.
    /// </summary>
    public static DesiredDeviceState Create(Guid deviceId)
    {
        return new DesiredDeviceState
        {
            DeviceId = deviceId,
            ComputedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5), // 5-minute TTL
            IsValid = false
        };
    }

    /// <summary>
    /// Check if this state has expired and needs recomputation.
    /// </summary>
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;

    /// <summary>
    /// Update computation hash after setting state values.
    /// Combine all state components for change detection.
    /// </summary>
    public void UpdateComputationHash()
    {
        var components = new List<string>
        {
            CurrentPolicyId?.ToString() ?? "null",
            CurrentPolicyVersion?.ToString() ?? "null",
            DesiredReleaseIdsString,
            AssignedTagIdsString
        };

        var combined = string.Join("|", components);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combined));
        ComputationHash = Convert.ToHexString(hash);
    }

    /// <summary>
    /// Check if state has changed based on computation hash.
    /// </summary>
    public bool HasChanged(DesiredDeviceState other)
    {
        if (other == null) return true;
        return ComputationHash != other.ComputationHash;
    }

    /// <summary>
    /// Refresh TTL (mark state valid for another 5 minutes).
    /// </summary>
    public void RefreshTTL()
    {
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        IsValid = true;
    }

    private static List<Guid> ParseIds(string idString)
    {
        if (string.IsNullOrEmpty(idString))
            return new List<Guid>();
        
        return idString.Split(';', System.StringSplitOptions.RemoveEmptyEntries)
            .Where(s => Guid.TryParse(s, out _))
            .Select(Guid.Parse)
            .ToList();
    }

    private static string FormatIds(List<Guid>? ids)
    {
        if (ids == null || ids.Count == 0)
            return string.Empty;
        
        return string.Join(";", ids.Select(id => id.ToString()));
    }
}

/// <summary>
/// Observed device state as reported by the device.
/// Used in reconciliation to compare against desired state.
/// </summary>
public class ObservedDeviceState
{
    /// <summary>Policy ID device reports as currently applied.</summary>
    public Guid? ObservedPolicyId { get; set; }

    /// <summary>Policy version device has acknowledged.</summary>
    public long? ObservedPolicyVersion { get; set; }

    /// <summary>Release IDs device reports as installed/acknowledged.</summary>
    public List<Guid> ObservedReleaseIds { get; set; } = new();

    /// <summary>Tag IDs device reports.</summary>
    public List<Guid> ObservedTagIds { get; set; } = new();

    /// <summary>Number of pending commands in device inbox.</summary>
    public int PendingCommandCount { get; set; }

    /// <summary>Number of pending events in device outbox.</summary>
    public int PendingEventCount { get; set; }

    /// <summary>Timestamp when device captured this state.</summary>
    public DateTimeOffset CapturedAt { get; set; }
}

/// <summary>
/// Result of reconciliation comparing desired vs observed state.
/// </summary>
public class ReconciliationResult
{
    /// <summary>Device ID being reconciled.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Desired state from server.</summary>
    public DesiredDeviceState DesiredState { get; set; } = new();

    /// <summary>Observed state from device.</summary>
    public ObservedDeviceState ObservedState { get; set; } = new();

    /// <summary>True if desired and observed states match.</summary>
    public bool IsInSync
    {
        get
        {
            return DesiredState?.CurrentPolicyId == ObservedState?.ObservedPolicyId
                && DesiredState?.CurrentPolicyVersion == ObservedState?.ObservedPolicyVersion
                && SetEquals(DesiredState?.DesiredReleaseIds, ObservedState?.ObservedReleaseIds)
                && SetEquals(DesiredState?.AssignedTagIds, ObservedState?.ObservedTagIds);
        }
    }

    /// <summary>Policy conflicts detected during reconciliation.</summary>
    public List<string> PolicyConflicts { get; set; } = new();

    /// <summary>Release conflicts detected during reconciliation.</summary>
    public List<string> ReleaseConflicts { get; set; } = new();

    /// <summary>Tag conflicts detected during reconciliation.</summary>
    public List<string> TagConflicts { get; set; } = new();

    /// <summary>Missing commands that device needs.</summary>
    public List<string> MissingCommands { get; set; } = new();

    /// <summary>Timestamp of reconciliation.</summary>
    public DateTimeOffset ReconciliedAt { get; set; } = DateTimeOffset.UtcNow;

    private static bool SetEquals(List<Guid>? desired, List<Guid>? observed)
    {
        if (desired == null || observed == null)
            return desired == observed;
        return desired.OrderBy(x => x).SequenceEqual(observed.OrderBy(x => x));
    }
}

/// <summary>
/// Enumeration of state change types for audit logging.
/// </summary>
public enum StateChangeType
{
    PolicyAssigned = 0,
    PolicyUnassigned = 1,
    PolicyVersionUpdated = 2,
    ReleaseAssigned = 3,
    ReleaseUnassigned = 4,
    TagAssigned = 5,
    TagUnassigned = 6,
    DeviceStateReconciled = 7,
    ConflictDetected = 8
}
