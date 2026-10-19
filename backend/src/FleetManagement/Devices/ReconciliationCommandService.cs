using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FleetManagement.Devices;

/// <summary>
/// Service for generating remediation commands from device reconciliation conflicts.
/// Analyzes state mismatches and creates specific commands to bring device to desired state.
/// </summary>
public sealed class ReconciliationCommandService
{
    private readonly FleetDbContext _db;

    public ReconciliationCommandService(FleetDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>
    /// Generate commands from a reconciliation result.
    /// Creates specific commands to remediate detected conflicts.
    /// </summary>
    /// <param name="reconciliationResult">Result from state reconciliation</param>
    /// <param name="auditLogId">ID of the audit log entry that triggered command generation</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of generated commands</returns>
    public async Task<List<ReconciliationCommand>> GenerateCommandsAsync(
        ReconciliationResult reconciliationResult,
        Guid? auditLogId = null,
        CancellationToken ct = default)
    {
        if (reconciliationResult == null)
            throw new ArgumentNullException(nameof(reconciliationResult));

        var commands = new List<ReconciliationCommand>();
        var desiredState = reconciliationResult.DesiredState;
        var observedState = reconciliationResult.ObservedState;

        // Get device info
        var device = await _db.Set<Device>()
            .FirstOrDefaultAsync(d => d.DeviceId == reconciliationResult.DeviceId, ct);

        if (device == null)
            throw new KeyNotFoundException($"Device {reconciliationResult.DeviceId} not found");

        // Generate policy command if policy mismatch
        if (desiredState.CurrentPolicyId != observedState.ObservedPolicyId)
        {
            var policyCommand = new ReconciliationCommand
            {
                Id = Guid.NewGuid(),
                DeviceId = reconciliationResult.DeviceId,
                EnrollmentId = device.EnrollmentId,
                AuditLogId = auditLogId,
                CommandType = ReconciliationCommandType.AssignPolicy,
                Status = ReconciliationCommandStatus.Pending,
                Priority = 10, // High priority
                ResourceId = desiredState.CurrentPolicyId,
                Description = $"Assign policy {desiredState.CurrentPolicyId} (v{desiredState.CurrentPolicyVersion})",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    policyId = desiredState.CurrentPolicyId,
                    version = desiredState.CurrentPolicyVersion
                }),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                MaxDeliveryAttempts = 5 // More retries for critical commands
            };
            commands.Add(policyCommand);
        }
        else if (desiredState.CurrentPolicyVersion != observedState.ObservedPolicyVersion)
        {
            // Version mismatch - still same policy but needs update
            var policyCommand = new ReconciliationCommand
            {
                Id = Guid.NewGuid(),
                DeviceId = reconciliationResult.DeviceId,
                EnrollmentId = device.EnrollmentId,
                AuditLogId = auditLogId,
                CommandType = ReconciliationCommandType.AssignPolicy,
                Status = ReconciliationCommandStatus.Pending,
                Priority = 15,
                ResourceId = desiredState.CurrentPolicyId,
                Description = $"Update policy to version {desiredState.CurrentPolicyVersion}",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    policyId = desiredState.CurrentPolicyId,
                    version = desiredState.CurrentPolicyVersion,
                    reason = "version_mismatch"
                }),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                MaxDeliveryAttempts = 5
            };
            commands.Add(policyCommand);
        }

        // Generate release commands
        var missingReleases = desiredState.DesiredReleaseIds
            .Except(observedState.ObservedReleaseIds)
            .ToList();

        var extraReleases = observedState.ObservedReleaseIds
            .Except(desiredState.DesiredReleaseIds)
            .ToList();

        // Assign missing releases
        foreach (var releaseId in missingReleases)
        {
            var releaseCommand = new ReconciliationCommand
            {
                Id = Guid.NewGuid(),
                DeviceId = reconciliationResult.DeviceId,
                EnrollmentId = device.EnrollmentId,
                AuditLogId = auditLogId,
                CommandType = ReconciliationCommandType.AssignRelease,
                Status = ReconciliationCommandStatus.Pending,
                Priority = 20,
                ResourceId = releaseId,
                Description = $"Install release {releaseId}",
                PayloadJson = JsonSerializer.Serialize(new { releaseId = releaseId }),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(14), // Longer expiry for release downloads
                MaxDeliveryAttempts = 3
            };
            commands.Add(releaseCommand);
        }

        // If device has extra releases, we might want to uninstall them (optional based on policy)
        if (extraReleases.Any())
        {
            // For now, just log them - uninstall could be destructive
            // Could be controlled by a policy flag
        }

        // Generate tag commands
        var missingTags = desiredState.AssignedTagIds
            .Except(observedState.ObservedTagIds)
            .ToList();

        var extraTags = observedState.ObservedTagIds
            .Except(desiredState.AssignedTagIds)
            .ToList();

        // Assign missing tags
        foreach (var tagId in missingTags)
        {
            var tagCommand = new ReconciliationCommand
            {
                Id = Guid.NewGuid(),
                DeviceId = reconciliationResult.DeviceId,
                EnrollmentId = device.EnrollmentId,
                AuditLogId = auditLogId,
                CommandType = ReconciliationCommandType.AssignTag,
                Status = ReconciliationCommandStatus.Pending,
                Priority = 30,
                ResourceId = tagId,
                Description = $"Assign tag {tagId}",
                PayloadJson = JsonSerializer.Serialize(new { tagId = tagId }),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                MaxDeliveryAttempts = 3
            };
            commands.Add(tagCommand);
        }

        // Unassign extra tags
        foreach (var tagId in extraTags)
        {
            var untagCommand = new ReconciliationCommand
            {
                Id = Guid.NewGuid(),
                DeviceId = reconciliationResult.DeviceId,
                EnrollmentId = device.EnrollmentId,
                AuditLogId = auditLogId,
                CommandType = ReconciliationCommandType.UnassignTag,
                Status = ReconciliationCommandStatus.Pending,
                Priority = 35,
                ResourceId = tagId,
                Description = $"Unassign tag {tagId}",
                PayloadJson = JsonSerializer.Serialize(new { tagId = tagId }),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                MaxDeliveryAttempts = 3
            };
            commands.Add(untagCommand);
        }

        // If reconciliation failed entirely, generate a verify command
        if (reconciliationResult.PolicyConflicts.Any() || reconciliationResult.ReleaseConflicts.Any())
        {
            var verifyCommand = new ReconciliationCommand
            {
                Id = Guid.NewGuid(),
                DeviceId = reconciliationResult.DeviceId,
                EnrollmentId = device.EnrollmentId,
                AuditLogId = auditLogId,
                CommandType = ReconciliationCommandType.VerifyState,
                Status = ReconciliationCommandStatus.Pending,
                Priority = 99, // Lowest priority - done after all other commands
                Description = "Verify device has reached desired state",
                PayloadJson = "{}",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(1),
                MaxDeliveryAttempts = 1,
                ScheduledFor = DateTime.UtcNow.AddMinutes(5) // Schedule after 5 min for others to complete
            };
            commands.Add(verifyCommand);
        }

        return commands;
    }

    /// <summary>
    /// Persist generated commands to database.
    /// </summary>
    /// <param name="commands">Commands to persist</param>
    /// <param name="ct">Cancellation token</param>
    public async Task PersistCommandsAsync(List<ReconciliationCommand> commands, CancellationToken ct = default)
    {
        if (commands == null || commands.Count == 0)
            return;

        _db.Set<ReconciliationCommand>().AddRange(commands);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Get pending commands ready for delivery to a device.
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    /// <param name="maxCommands">Maximum commands to return</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of pending commands ordered by priority</returns>
    public async Task<List<ReconciliationCommand>> GetPendingCommandsAsync(
        Guid deviceId,
        int maxCommands = 10,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var commands = await _db.Set<ReconciliationCommand>()
            .Where(c => c.DeviceId == deviceId)
            .Where(c => c.Status == ReconciliationCommandStatus.Pending)
            .Where(c => !c.ExpiresAt.HasValue || c.ExpiresAt.Value > now)
            .Where(c => !c.ScheduledFor.HasValue || c.ScheduledFor.Value <= now)
            .Where(c => c.DeliveryAttempts < c.MaxDeliveryAttempts)
            .OrderBy(c => c.Priority)
            .ThenBy(c => c.CreatedAt)
            .Take(maxCommands)
            .ToListAsync(ct);

        return commands;
    }

    /// <summary>
    /// Update command status after device response.
    /// </summary>
    /// <param name="commandId">Command ID</param>
    /// <param name="newStatus">New status</param>
    /// <param name="executionResult">Result/response from device (optional)</param>
    /// <param name="errorMessage">Error message if failed (optional)</param>
    /// <param name="ct">Cancellation token</param>
    public async Task UpdateCommandStatusAsync(
        Guid commandId,
        ReconciliationCommandStatus newStatus,
        string? executionResult = null,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        var command = await _db.Set<ReconciliationCommand>()
            .FirstOrDefaultAsync(c => c.Id == commandId, ct);

        if (command == null)
            throw new KeyNotFoundException($"Command {commandId} not found");

        command.Status = newStatus;
        command.LastStatusUpdate = DateTime.UtcNow;

        if (newStatus == ReconciliationCommandStatus.Executed || newStatus == ReconciliationCommandStatus.Failed)
        {
            command.CompletedAt = DateTime.UtcNow;
        }

        if (!string.IsNullOrEmpty(executionResult))
            command.ExecutionResultJson = executionResult;

        if (!string.IsNullOrEmpty(errorMessage))
            command.ErrorMessage = errorMessage;

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Increment delivery attempt counter.
    /// </summary>
    /// <param name="commandId">Command ID</param>
    /// <param name="ct">Cancellation token</param>
    public async Task IncrementDeliveryAttemptAsync(Guid commandId, CancellationToken ct = default)
    {
        var command = await _db.Set<ReconciliationCommand>()
            .FirstOrDefaultAsync(c => c.Id == commandId, ct);

        if (command == null)
            throw new KeyNotFoundException($"Command {commandId} not found");

        command.DeliveryAttempts++;
        command.LastStatusUpdate = DateTime.UtcNow;

        if (command.IsAbandoned && command.Status == ReconciliationCommandStatus.Pending)
        {
            command.Status = ReconciliationCommandStatus.Failed;
            command.CompletedAt = DateTime.UtcNow;
            command.ErrorMessage = "Delivery attempts exhausted";
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Get command statistics for a device.
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Command statistics</returns>
    public async Task<CommandStatistics> GetCommandStatisticsAsync(Guid deviceId, CancellationToken ct = default)
    {
        var commands = await _db.Set<ReconciliationCommand>()
            .Where(c => c.DeviceId == deviceId)
            .ToListAsync(ct);

        return new CommandStatistics
        {
            TotalCommands = commands.Count,
            Pending = commands.Count(c => c.Status == ReconciliationCommandStatus.Pending),
            Delivered = commands.Count(c => c.Status == ReconciliationCommandStatus.Delivered),
            Executed = commands.Count(c => c.Status == ReconciliationCommandStatus.Executed),
            Failed = commands.Count(c => c.Status == ReconciliationCommandStatus.Failed),
            Expired = commands.Count(c => c.Status == ReconciliationCommandStatus.Expired),
            Cancelled = commands.Count(c => c.Status == ReconciliationCommandStatus.Cancelled),
            AverageDeliveryAttempts = commands.Count > 0 ? commands.Average(c => c.DeliveryAttempts) : 0,
            OldestPendingCommand = commands
                .Where(c => c.Status == ReconciliationCommandStatus.Pending)
                .MinBy(c => c.CreatedAt)
                ?.CreatedAt
        };
    }
}

/// <summary>
/// Statistics for commands targeting a specific device.
/// </summary>
public class CommandStatistics
{
    public int TotalCommands { get; set; }
    public int Pending { get; set; }
    public int Delivered { get; set; }
    public int Executed { get; set; }
    public int Failed { get; set; }
    public int Expired { get; set; }
    public int Cancelled { get; set; }
    public double AverageDeliveryAttempts { get; set; }
    public DateTime? OldestPendingCommand { get; set; }
}
