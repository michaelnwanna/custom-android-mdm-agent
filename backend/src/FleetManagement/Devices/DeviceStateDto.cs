namespace FleetManagement.Devices;

using System;
using System.Collections.Generic;

/// <summary>
/// DTO for desired device state returned by the REST API.
/// </summary>
public class DesiredDeviceStateDto
{
    /// <summary>Device ID.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Current policy ID (null if no policy assigned).</summary>
    public Guid? CurrentPolicyId { get; set; }

    /// <summary>Current policy version.</summary>
    public long? CurrentPolicyVersion { get; set; }

    /// <summary>List of assigned release IDs.</summary>
    public List<Guid> DesiredReleaseIds { get; set; } = new();

    /// <summary>List of assigned tag IDs.</summary>
    public List<Guid> AssignedTagIds { get; set; } = new();

    /// <summary>When this state was last computed.</summary>
    public DateTimeOffset ComputedAt { get; set; }

    /// <summary>Computation hash for change detection.</summary>
    public string ComputationHash { get; set; } = string.Empty;

    /// <summary>Whether this state is valid (has been persisted).</summary>
    public bool IsValid { get; set; }

    /// <summary>When this state expires (needs recomputation).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Whether this state has expired.</summary>
    public bool IsExpired { get; set; }

    /// <summary>Factory: Create DTO from domain model.</summary>
    public static DesiredDeviceStateDto FromDomain(DesiredDeviceState domain)
    {
        return new DesiredDeviceStateDto
        {
            DeviceId = domain.DeviceId,
            CurrentPolicyId = domain.CurrentPolicyId,
            CurrentPolicyVersion = domain.CurrentPolicyVersion,
            DesiredReleaseIds = domain.DesiredReleaseIds,
            AssignedTagIds = domain.AssignedTagIds,
            ComputedAt = domain.ComputedAt,
            ComputationHash = domain.ComputationHash,
            IsValid = domain.IsValid,
            ExpiresAt = domain.ExpiresAt,
            IsExpired = domain.IsExpired
        };
    }
}

/// <summary>
/// DTO for observed device state in reconciliation requests.
/// </summary>
public class ObservedDeviceStateDto
{
    /// <summary>Policy ID device reports as currently applied.</summary>
    public Guid? ObservedPolicyId { get; set; }

    /// <summary>Policy version device has acknowledged.</summary>
    public long? ObservedPolicyVersion { get; set; }

    /// <summary>Release IDs device reports as installed.</summary>
    public List<Guid> ObservedReleaseIds { get; set; } = new();

    /// <summary>Tag IDs device reports.</summary>
    public List<Guid> ObservedTagIds { get; set; } = new();

    /// <summary>Number of pending commands in device inbox.</summary>
    public int PendingCommandCount { get; set; }

    /// <summary>Number of pending events in device outbox.</summary>
    public int PendingEventCount { get; set; }

    /// <summary>Timestamp when device captured this state.</summary>
    public DateTimeOffset CapturedAt { get; set; }

    /// <summary>Factory: Create domain model from DTO.</summary>
    public ObservedDeviceState ToDomain()
    {
        return new ObservedDeviceState
        {
            ObservedPolicyId = ObservedPolicyId,
            ObservedPolicyVersion = ObservedPolicyVersion,
            ObservedReleaseIds = ObservedReleaseIds,
            ObservedTagIds = ObservedTagIds,
            PendingCommandCount = PendingCommandCount,
            PendingEventCount = PendingEventCount,
            CapturedAt = CapturedAt
        };
    }
}

/// <summary>
/// Request DTO for reconciliation endpoint.
/// </summary>
public class ReconciliationRequestDto
{
    /// <summary>Device's current observed state.</summary>
    public ObservedDeviceStateDto ObservedState { get; set; } = new();
}

/// <summary>
/// Result DTO for reconciliation response.
/// </summary>
public class ReconciliationResultDto
{
    /// <summary>Device ID being reconciled.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Desired state from server.</summary>
    public DesiredDeviceStateDto DesiredState { get; set; } = new();

    /// <summary>Observed state from device.</summary>
    public ObservedDeviceStateDto ObservedState { get; set; } = new();

    /// <summary>Whether desired and observed states match.</summary>
    public bool IsInSync { get; set; }

    /// <summary>Policy conflicts detected.</summary>
    public List<string> PolicyConflicts { get; set; } = new();

    /// <summary>Release conflicts detected.</summary>
    public List<string> ReleaseConflicts { get; set; } = new();

    /// <summary>Tag conflicts detected.</summary>
    public List<string> TagConflicts { get; set; } = new();

    /// <summary>Missing commands that device needs.</summary>
    public List<string> MissingCommands { get; set; } = new();

    /// <summary>Timestamp of reconciliation.</summary>
    public DateTimeOffset ReconciliedAt { get; set; }

    /// <summary>Factory: Create DTO from domain model.</summary>
    public static ReconciliationResultDto FromDomain(ReconciliationResult domain)
    {
        return new ReconciliationResultDto
        {
            DeviceId = domain.DeviceId,
            DesiredState = DesiredDeviceStateDto.FromDomain(domain.DesiredState),
            ObservedState = new ObservedDeviceStateDto
            {
                ObservedPolicyId = domain.ObservedState?.ObservedPolicyId,
                ObservedPolicyVersion = domain.ObservedState?.ObservedPolicyVersion,
                ObservedReleaseIds = domain.ObservedState?.ObservedReleaseIds ?? new(),
                ObservedTagIds = domain.ObservedState?.ObservedTagIds ?? new(),
                PendingCommandCount = domain.ObservedState?.PendingCommandCount ?? 0,
                PendingEventCount = domain.ObservedState?.PendingEventCount ?? 0,
                CapturedAt = domain.ObservedState?.CapturedAt ?? DateTimeOffset.UtcNow
            },
            IsInSync = domain.IsInSync,
            PolicyConflicts = domain.PolicyConflicts,
            ReleaseConflicts = domain.ReleaseConflicts,
            TagConflicts = domain.TagConflicts,
            MissingCommands = domain.MissingCommands,
            ReconciliedAt = domain.ReconciliedAt
        };
    }
}
