namespace FleetManagement.Devices;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FleetManagement.Persistence;
using FleetManagement.Policies;
using FleetManagement.Releases;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Service for computing and caching desired device state.
/// Aggregates current policy, releases, and tags for a device.
/// Provides reconciliation support for offline-first scenarios.
/// </summary>
public sealed class StateComputationService
{
    private readonly FleetDbContext _db;
    private readonly ReleaseService _releaseService;

    public StateComputationService(FleetDbContext db, ReleaseService releaseService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _releaseService = releaseService ?? throw new ArgumentNullException(nameof(releaseService));
    }

    /// <summary>
    /// Compute desired state for a device from current assignments.
    /// Resolves policies, releases, and tags; detects conflicts.
    /// </summary>
    /// <param name="deviceId">Device to compute state for</param>
    /// <param name="forceRecompute">Ignore cache and recompute</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Computed DesiredDeviceState with validation status</returns>
    public async Task<DesiredDeviceState> ComputeDesiredStateAsync(
        Guid deviceId,
        bool forceRecompute = false,
        CancellationToken ct = default)
    {
        if (deviceId == Guid.Empty)
            throw new ArgumentException("Device ID required", nameof(deviceId));

        // Check cache first
        var cached = await _db.Set<DesiredDeviceState>()
            .FirstOrDefaultAsync(s => s.DeviceId == deviceId, ct);

        if (cached != null && !cached.IsExpired && !forceRecompute && cached.IsValid)
        {
            return cached;
        }

        // Verify device exists
        var device = await _db.Set<Device>()
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DeviceId == deviceId, ct);

        if (device == null)
            throw new KeyNotFoundException($"Device {deviceId} not found");

        // Compute current policy state
        var policyAssignment = await _db.Set<PolicyAssignment>()
            .AsNoTracking()
            .Where(pa => pa.EnrollmentId == device.EnrollmentId)
            .OrderByDescending(pa => pa.AssignedAt)
            .FirstOrDefaultAsync(ct);

        Guid? currentPolicyId = null;
        long? currentPolicyVersion = null;

        if (policyAssignment != null)
        {
            var policy = await _db.Set<Policy>()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PolicyId == policyAssignment.PolicyId && p.IsActive, ct);

            if (policy != null)
            {
                currentPolicyId = policyAssignment.PolicyId;
                currentPolicyVersion = policyAssignment.AssignedVersion;
            }
        }

        // Compute desired releases (active deployments assigned to this enrollment)
        var desiredReleases = await _db.Set<DeploymentDeviceState>()
            .AsNoTracking()
            .Where(dds => dds.EnrollmentId == device.EnrollmentId)
            // Only include devices in non-failed/non-completed states (actively being deployed)
            .Where(dds => dds.Status == DeploymentDeviceStatus.Pending 
                || dds.Status == DeploymentDeviceStatus.Downloading 
                || dds.Status == DeploymentDeviceStatus.Installing
                || dds.Status == DeploymentDeviceStatus.Completed)
            .Select(dds => dds.DeploymentId)
            .Distinct()
            .ToListAsync(ct);

        // Compute assigned tags
        var assignedTags = await _db.Set<DeviceTagAssignment>()
            .AsNoTracking()
            .Where(dta => dta.DeviceId == deviceId && dta.RemovedAt == null)
            .Select(dta => dta.TagId)
            .ToListAsync(ct);

        // Build or update state
        if (cached == null)
        {
            cached = DesiredDeviceState.Create(deviceId);
        }

        cached.CurrentPolicyId = currentPolicyId;
        cached.CurrentPolicyVersion = currentPolicyVersion;
        cached.DesiredReleaseIds = desiredReleases;
        cached.AssignedTagIds = assignedTags;
        cached.ComputedAt = DateTimeOffset.UtcNow;
        cached.UpdateComputationHash();
        cached.IsValid = true;
        cached.RefreshTTL();

        // Persist computed state
        var existing = await _db.Set<DesiredDeviceState>()
            .FirstOrDefaultAsync(s => s.DeviceId == deviceId, ct);

        if (existing != null)
        {
            existing.CurrentPolicyId = cached.CurrentPolicyId;
            existing.CurrentPolicyVersion = cached.CurrentPolicyVersion;
            existing.DesiredReleaseIds = cached.DesiredReleaseIds;
            existing.AssignedTagIds = cached.AssignedTagIds;
            existing.ComputedAt = cached.ComputedAt;
            existing.ComputationHash = cached.ComputationHash;
            existing.IsValid = cached.IsValid;
            existing.ExpiresAt = cached.ExpiresAt;
        }
        else
        {
            _db.Set<DesiredDeviceState>().Add(cached);
        }

        await _db.SaveChangesAsync(ct);

        return cached;
    }

    /// <summary>
    /// Get cached desired state without recomputation.
    /// Returns null if not cached or expired.
    /// </summary>
    public async Task<DesiredDeviceState?> GetCachedStateAsync(Guid deviceId, CancellationToken ct = default)
    {
        var state = await _db.Set<DesiredDeviceState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.DeviceId == deviceId, ct);

        if (state != null && !state.IsExpired && state.IsValid)
            return state;

        return null;
    }

    /// <summary>
    /// Invalidate cached state (force recompute on next call).
    /// Called when assignments change.
    /// </summary>
    public async Task InvalidateCacheAsync(Guid deviceId, CancellationToken ct = default)
    {
        var state = await _db.Set<DesiredDeviceState>()
            .FirstOrDefaultAsync(s => s.DeviceId == deviceId, ct);

        if (state != null)
        {
            state.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1); // Expire immediately
            state.IsValid = false;
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Invalidate all cached states (system-wide invalidation).
    /// </summary>
    public async Task InvalidateAllCachesAsync(CancellationToken ct = default)
    {
        await _db.Set<DesiredDeviceState>()
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsValid, false)
                .SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)), ct);
    }

    /// <summary>
    /// Perform reconciliation between desired and observed state.
    /// Identify conflicts and missing commands.
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    /// <param name="observedState">State reported by device</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>ReconciliationResult with differences and conflicts</returns>
    public async Task<ReconciliationResult> ReconcileStateAsync(
        Guid deviceId,
        ObservedDeviceState observedState,
        CancellationToken ct = default)
    {
        if (deviceId == Guid.Empty)
            throw new ArgumentException("Device ID required", nameof(deviceId));

        if (observedState == null)
            throw new ArgumentNullException(nameof(observedState));

        // Compute desired state
        var desiredState = await ComputeDesiredStateAsync(deviceId, forceRecompute: false, ct);

        var result = new ReconciliationResult
        {
            DeviceId = deviceId,
            DesiredState = desiredState,
            ObservedState = observedState
        };

        // Check policy conflicts
        if (desiredState.CurrentPolicyId != observedState.ObservedPolicyId)
        {
            result.PolicyConflicts.Add(
                $"Policy mismatch: desired={desiredState.CurrentPolicyId}, observed={observedState.ObservedPolicyId}"
            );
            result.MissingCommands.Add($"ASSIGN_POLICY:{desiredState.CurrentPolicyId}");
        }
        else if (desiredState.CurrentPolicyVersion != observedState.ObservedPolicyVersion)
        {
            result.PolicyConflicts.Add(
                $"Policy version mismatch: desired v{desiredState.CurrentPolicyVersion}, observed v{observedState.ObservedPolicyVersion}"
            );
            result.MissingCommands.Add($"ASSIGN_POLICY:{desiredState.CurrentPolicyId}");
        }

        // Check release conflicts
        var missingReleases = desiredState.DesiredReleaseIds
            .Except(observedState.ObservedReleaseIds)
            .ToList();

        var extraReleases = observedState.ObservedReleaseIds
            .Except(desiredState.DesiredReleaseIds)
            .ToList();

        if (missingReleases.Any())
        {
            result.ReleaseConflicts.Add(
                $"Missing releases: {string.Join(", ", missingReleases)}"
            );
            foreach (var releaseId in missingReleases)
            {
                result.MissingCommands.Add($"ASSIGN_RELEASE:{releaseId}");
            }
        }

        if (extraReleases.Any())
        {
            result.ReleaseConflicts.Add(
                $"Extra releases: {string.Join(", ", extraReleases)}"
            );
        }

        // Check tag conflicts
        var missingTags = desiredState.AssignedTagIds
            .Except(observedState.ObservedTagIds)
            .ToList();

        var extraTags = observedState.ObservedTagIds
            .Except(desiredState.AssignedTagIds)
            .ToList();

        if (missingTags.Any() || extraTags.Any())
        {
            result.TagConflicts.Add(
                $"Tags mismatch: missing={missingTags.Count}, extra={extraTags.Count}"
            );
            result.MissingCommands.Add($"SYNC_TAGS");
        }

        return result;
    }

    /// <summary>
    /// Get all devices that are out of sync (desired ≠ observed).
    /// For monitoring and alert generation.
    /// </summary>
    public async Task<List<Guid>> GetOutOfSyncDevicesAsync(TimeSpan maxOfflineDuration, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(maxOfflineDuration);

        return await _db.Set<Device>()
            .AsNoTracking()
            .Where(d => d.LastSeenAt < cutoff) // Haven't reported recent state
            .Select(d => d.DeviceId)
            .ToListAsync(ct);
    }
}
