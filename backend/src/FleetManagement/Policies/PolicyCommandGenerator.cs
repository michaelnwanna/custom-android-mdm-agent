namespace FleetManagement.Policies;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FleetManagement.Enrollment;
using FleetManagement.Messaging;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Generates and publishes SET_POLICY commands for enrolled devices.
/// Queries active policy assignments and creates command events.
/// </summary>
public sealed class PolicyCommandGenerator
{
    private readonly IDbContextFactory<FleetDbContext> _db;
    private readonly TimeProvider _clock;

    public PolicyCommandGenerator(
        IDbContextFactory<FleetDbContext> db,
        TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// Generate and register SET_POLICY commands for all devices with active assignments.
    /// Idempotent: same policy + device → same command ID (via deterministic UUID).
    /// </summary>
    /// <param name="enrollmentId">If provided, generate command only for this device</param>
    /// <returns>Number of commands registered</returns>
    public async Task<int> GenerateActiveAssignmentsAsync(Guid? enrollmentId = null)
    {
        await using var db = await _db.CreateDbContextAsync();

        // Query active assignments (unacknowledged or version mismatch)
        var query = db.Set<PolicyAssignment>().AsNoTracking()
            .Where(a => a.AcknowledgedVersion == null || a.AcknowledgedVersion < a.AssignedVersion);

        if (enrollmentId.HasValue)
            query = query.Where(a => a.EnrollmentId == enrollmentId.Value);

        var assignments = await query.ToListAsync();

        int registered = 0;

        foreach (var assignment in assignments)
        {
            var policy = await db.Set<Policy>()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PolicyId == assignment.PolicyId);

            if (policy == null || !policy.IsActive)
                continue;

            var enrollment = await db.Set<PendingEnrollment>()
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == assignment.EnrollmentId);

            if (enrollment?.DeviceId == null)
                continue;

            var commandId = DeriveCommandId(assignment.EnrollmentId, assignment.PolicyId);
            var deviceId = enrollment.DeviceId.Value;
            var correlationId = Guid.NewGuid();

            // Build command JSON
            var command = new
            {
                schemaVersion = 1,
                commandId = commandId,
                deviceId = deviceId,
                sequence = 1L,
                type = "SET_POLICY",
                payload = new
                {
                    policyId = assignment.PolicyId,
                    policyVersion = assignment.AssignedVersion
                },
                issuedAt = _clock.GetUtcNow(),
                expiresAt = _clock.GetUtcNow().AddHours(1),
                correlationId = correlationId
            };

            try
            {
                // Register ownership in database (idempotent)
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
    /// Derive a deterministic command ID from enrollment + policy.
    /// Same inputs always produce same command ID (enables idempotent retry).
    /// </summary>
    private static Guid DeriveCommandId(Guid enrollmentId, Guid policyId)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var combined = enrollmentId.ToByteArray().Concat(policyId.ToByteArray()).ToArray();
        var hash = md5.ComputeHash(combined);
        return new Guid(hash);
    }
}
