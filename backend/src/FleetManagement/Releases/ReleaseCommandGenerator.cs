namespace FleetManagement.Releases;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FleetManagement.Enrollment;
using FleetManagement.Messaging;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Generates and publishes INSTALL_RELEASE commands for enrolled devices.
/// Queries active deployment assignments and creates command events.
/// </summary>
public sealed class ReleaseCommandGenerator
{
    private readonly IDbContextFactory<FleetDbContext> _db;
    private readonly TimeProvider _clock;

    public ReleaseCommandGenerator(
        IDbContextFactory<FleetDbContext> db,
        TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// Generate and register INSTALL_RELEASE commands for all devices with active deployment assignments.
    /// Idempotent: same deployment + device → same command ID (via deterministic UUID).
    /// </summary>
    /// <param name="enrollmentId">If provided, generate command only for this device</param>
    /// <returns>Number of commands registered</returns>
    public async Task<int> GenerateActiveAssignmentsAsync(Guid? enrollmentId = null)
    {
        await using var db = await _db.CreateDbContextAsync();

        // Query active device deployment states (Pending or Downloading/Installing status)
        var query = db.Set<DeploymentDeviceState>().AsNoTracking()
            .Where(s => s.Status == DeploymentDeviceStatus.Pending ||
                       s.Status == DeploymentDeviceStatus.Downloading ||
                       s.Status == DeploymentDeviceStatus.Installing);

        if (enrollmentId.HasValue)
            query = query.Where(s => s.EnrollmentId == enrollmentId.Value);

        var assignments = await query.ToListAsync();

        int registered = 0;

        foreach (var assignment in assignments)
        {
            // Get the deployment
            var deployment = await db.Set<ReleaseDeployment>()
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeploymentId == assignment.DeploymentId);

            if (deployment == null || !deployment.IsActive)
                continue;

            // Get the release
            var release = await db.Set<Release>()
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.ReleaseId == deployment.ReleaseId);

            if (release == null || !release.IsActive)
                continue;

            // Get the enrollment
            var enrollment = await db.Set<PendingEnrollment>()
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == assignment.EnrollmentId);

            if (enrollment?.DeviceId == null)
                continue;

            // Derive deterministic command ID
            var commandId = DeriveCommandId(assignment.DeploymentId, assignment.EnrollmentId);
            var deviceId = enrollment.DeviceId.Value;
            var correlationId = Guid.NewGuid();

            // Build INSTALL_RELEASE command JSON
            var command = new
            {
                schemaVersion = 1,
                commandId = commandId,
                deviceId = deviceId,
                sequence = 1L,
                type = "INSTALL_RELEASE",
                payload = new
                {
                    releaseId = release.ReleaseId,
                    packageName = release.PackageName,
                    versionCode = release.VersionCode,
                    apkSha256 = release.ApkSha256,
                    apkSizeBytes = release.ApkSizeBytes,
                    minSdkLevel = release.MinSdkLevel,
                    downloadUrl = $"/api/v1/releases/{release.ReleaseId}/apk",
                    metadataSignature = release.MetadataSignature
                },
                issuedAt = _clock.GetUtcNow(),
                expiresAt = release.ExpiresAt,
                correlationId = correlationId
            };

            try
            {
                // Register command ownership in database (idempotent)
                await RegisterCommandOwnershipAsync(db, commandId, deviceId, correlationId, assignment.EnrollmentId);
                registered++;
            }
            catch (CommandEventRejectedException)
            {
                // Command already registered; skip
            }
        }

        return registered;
    }

    /// <summary>
    /// Generate INSTALL_RELEASE command for a specific deployment and device.
    /// Useful for targeted command generation (e.g., device retries).
    /// </summary>
    public async Task<int> GenerateForDeploymentAndDeviceAsync(Guid deploymentId, Guid enrollmentId)
    {
        await using var db = await _db.CreateDbContextAsync();

        // Get the device state
        var assignment = await db.Set<DeploymentDeviceState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.DeploymentId == deploymentId && s.EnrollmentId == enrollmentId);

        if (assignment == null || (assignment.Status != DeploymentDeviceStatus.Pending &&
                                   assignment.Status != DeploymentDeviceStatus.Downloading &&
                                   assignment.Status != DeploymentDeviceStatus.Installing))
            return 0;

        // Get the deployment
        var deployment = await db.Set<ReleaseDeployment>()
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DeploymentId == deploymentId);

        if (deployment == null || !deployment.IsActive)
            return 0;

        // Get the release
        var release = await db.Set<Release>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReleaseId == deployment.ReleaseId);

        if (release == null || !release.IsActive)
            return 0;

        // Get the enrollment
        var enrollment = await db.Set<PendingEnrollment>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == enrollmentId);

        if (enrollment?.DeviceId == null)
            return 0;

        var commandId = DeriveCommandId(deploymentId, enrollmentId);
        var deviceId = enrollment.DeviceId.Value;
        var correlationId = Guid.NewGuid();

        try
        {
            await RegisterCommandOwnershipAsync(db, commandId, deviceId, correlationId, enrollmentId);
            return 1;
        }
        catch (CommandEventRejectedException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Register command ownership in the database.
    /// Throws CommandEventRejectedException if conflict detected.
    /// </summary>
    private static async Task RegisterCommandOwnershipAsync(
        FleetDbContext db,
        Guid commandId,
        Guid deviceId,
        Guid correlationId,
        Guid enrollmentId)
    {
        var existing = await db.Set<CommandEventOwnership>()
            .FirstOrDefaultAsync(x => x.CommandId == commandId);

        if (existing != null)
        {
            // Verify consistency
            if (existing.DeviceId != deviceId ||
                existing.CorrelationId != correlationId ||
                existing.EnrollmentId != enrollmentId)
            {
                throw new CommandEventRejectedException();
            }
            // Otherwise it's a duplicate register (idempotent)
            return;
        }

        // Use reflection to call CommandEventOwnership.Create (internal method)
        var createMethod = typeof(CommandEventOwnership)
            .GetMethod("Create", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        if (createMethod == null)
            throw new InvalidOperationException("Cannot create CommandEventOwnership");

        var result = createMethod.Invoke(null, new object[] { commandId, deviceId, correlationId, enrollmentId });
        if (result is not CommandEventOwnership ownership)
            throw new InvalidOperationException("Failed to create ownership");

        db.Set<CommandEventOwnership>().Add(ownership);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Derive a deterministic command ID from deployment + enrollment.
    /// Same inputs always produce same command ID (enables idempotent retry).
    /// </summary>
    private static Guid DeriveCommandId(Guid deploymentId, Guid enrollmentId)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var combined = deploymentId.ToByteArray().Concat(enrollmentId.ToByteArray()).ToArray();
        var hash = md5.ComputeHash(combined);
        return new Guid(hash);
    }
}
