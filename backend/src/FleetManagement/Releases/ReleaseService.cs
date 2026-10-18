using System;
using System.Threading.Tasks;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FleetManagement.Releases;

/// <summary>
/// Service for release and deployment management.
/// Handles idempotent release creation, deployment workflows, and progress tracking.
/// </summary>
public sealed class ReleaseService
{
    private readonly FleetDbContext _db;

    public ReleaseService(FleetDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Create or retrieve a release. Idempotent: same metadata JSON produces same release ID.
    /// </summary>
    /// <param name="metadataJson">Release metadata as JSON (compact, exact bytes used for signature verification)</param>
    /// <param name="metadataSignature">Release metadata signature</param>
    /// <param name="creator">Subject creating the release (from OIDC token)</param>
    /// <returns>The release ID</returns>
    /// <exception cref="ArgumentException">If metadata is invalid or signature is invalid</exception>
    public async Task<Guid> CreateReleaseIdempotentAsync(
        string metadataJson,
        string metadataSignature,
        string creator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataSignature);
        ArgumentException.ThrowIfNullOrWhiteSpace(creator);

        var metadata = ReleaseMetadata.FromJson(metadataJson);
        var release = Release.CreateFromMetadata(metadata, metadataJson, metadataSignature, DateTimeOffset.UtcNow, creator);

        // Check if release already exists
        var existing = await _db.Set<Release>()
            .FirstOrDefaultAsync(r => r.ReleaseId == release.ReleaseId);

        if (existing != null)
        {
            // Verify it's the same metadata (idempotent check)
            if (existing.MetadataJson != metadataJson || existing.MetadataSignature != metadataSignature)
                throw new ArgumentException("Release ID conflict: different metadata with same ID");
            return existing.ReleaseId;
        }

        _db.Add(release);
        await _db.SaveChangesAsync();
        return release.ReleaseId;
    }

    /// <summary>
    /// Get a release by ID.
    /// </summary>
    public async Task<Release?> GetReleaseAsync(Guid releaseId)
    {
        return await _db.Set<Release>()
            .FirstOrDefaultAsync(r => r.ReleaseId == releaseId);
    }

    /// <summary>
    /// Create a pilot deployment for a release.
    /// Pilot deployments are for testing with a limited set of devices.
    /// </summary>
    public async Task<Guid> CreatePilotDeploymentAsync(
        Guid releaseId,
        string creator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(creator);
        if (releaseId == Guid.Empty)
            throw new ArgumentException("Release ID required");

        var release = await GetReleaseAsync(releaseId);
        if (release == null)
            throw new ArgumentException($"Release {releaseId} not found");
        if (!release.IsActive)
            throw new ArgumentException("Release is not active");

        var deployment = ReleaseDeployment.CreatePilot(releaseId, DateTimeOffset.UtcNow, creator);
        _db.Add(deployment);
        await _db.SaveChangesAsync();
        return deployment.DeploymentId;
    }

    /// <summary>
    /// Create a staged deployment for a release with percentage-based rollout.
    /// </summary>
    public async Task<Guid> CreateStagedDeploymentAsync(
        Guid releaseId,
        int rolloutPercentage,
        string creator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(creator);
        if (releaseId == Guid.Empty)
            throw new ArgumentException("Release ID required");
        if (rolloutPercentage <= 0 || rolloutPercentage > 100)
            throw new ArgumentException("Rollout percentage must be 1-100");

        var release = await GetReleaseAsync(releaseId);
        if (release == null)
            throw new ArgumentException($"Release {releaseId} not found");
        if (!release.IsActive)
            throw new ArgumentException("Release is not active");

        var deployment = ReleaseDeployment.CreateStaged(releaseId, rolloutPercentage, DateTimeOffset.UtcNow, creator);
        _db.Add(deployment);
        await _db.SaveChangesAsync();
        return deployment.DeploymentId;
    }

    /// <summary>
    /// Get a deployment by ID.
    /// </summary>
    public async Task<ReleaseDeployment?> GetDeploymentAsync(Guid deploymentId)
    {
        return await _db.Set<ReleaseDeployment>()
            .FirstOrDefaultAsync(d => d.DeploymentId == deploymentId);
    }

    /// <summary>
    /// Promote a staged deployment to production.
    /// </summary>
    public async Task<Guid> PromoteStagedDeploymentAsync(Guid deploymentId)
    {
        if (deploymentId == Guid.Empty)
            throw new ArgumentException("Deployment ID required");

        var deployment = await GetDeploymentAsync(deploymentId);
        if (deployment == null)
            throw new ArgumentException($"Deployment {deploymentId} not found");

        deployment.PromoteInPlace();
        _db.Update(deployment);
        await _db.SaveChangesAsync();
        return deployment.DeploymentId;
    }

    /// <summary>
    /// Assign a deployment to an enrolled device.
    /// Creates a pending deployment device state for the device.
    /// </summary>
    public async Task<Guid> AssignDeploymentAsync(
        Guid deploymentId,
        Guid enrollmentId)
    {
        if (deploymentId == Guid.Empty || enrollmentId == Guid.Empty)
            throw new ArgumentException("Deployment and enrollment IDs required");

        var deployment = await GetDeploymentAsync(deploymentId);
        if (deployment == null)
            throw new ArgumentException($"Deployment {deploymentId} not found");
        if (!deployment.IsActive)
            throw new ArgumentException("Deployment is not active");

        // Verify enrollment exists and has device ID
        var enrollment = await _db.Set<Enrollment.PendingEnrollment>()
            .FirstOrDefaultAsync(e => e.Id == enrollmentId);
        if (enrollment == null)
            throw new ArgumentException($"Enrollment {enrollmentId} not found");
        if (enrollment.DeviceId == null)
            throw new ArgumentException("Enrollment does not have a claimed device");

        // Check if device already has state for this deployment
        var existing = await _db.Set<DeploymentDeviceState>()
            .FirstOrDefaultAsync(s => s.DeploymentId == deploymentId && s.EnrollmentId == enrollmentId);
        if (existing != null)
            return existing.StateId;

        var deviceState = DeploymentDeviceState.CreatePending(deploymentId, enrollmentId, DateTimeOffset.UtcNow);
        _db.Add(deviceState);
        await _db.SaveChangesAsync();
        return deviceState.StateId;
    }

    /// <summary>
    /// Get device deployment state.
    /// </summary>
    public async Task<DeploymentDeviceState?> GetDeviceDeploymentStateAsync(Guid deploymentId, Guid enrollmentId)
    {
        return await _db.Set<DeploymentDeviceState>()
            .FirstOrDefaultAsync(s => s.DeploymentId == deploymentId && s.EnrollmentId == enrollmentId);
    }

    /// <summary>
    /// Update device deployment state based on progress event from device.
    /// </summary>
    public async Task UpdateDeviceDeploymentStateAsync(
        Guid deploymentId,
        Guid enrollmentId,
        DeploymentDeviceStatus newStatus,
        string? errorMessage = null)
    {
        var state = await GetDeviceDeploymentStateAsync(deploymentId, enrollmentId);
        if (state == null)
            throw new ArgumentException($"Device state for deployment {deploymentId} and enrollment {enrollmentId} not found");

        state.UpdateStatus(newStatus, errorMessage, DateTimeOffset.UtcNow);
        _db.Update(state);
        await _db.SaveChangesAsync();
    }
}
