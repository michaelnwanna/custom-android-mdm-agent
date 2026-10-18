namespace FleetManagement.Releases;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FleetManagement.Devices;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Service for bulk assignment operations targeting devices by tags.
/// Enables complex targeting scenarios (AND/OR logic) and tracks results.
/// </summary>
public sealed class BulkAssignmentService
{
    private readonly FleetDbContext _db;
    private readonly DeviceTagService _tagService;
    private readonly ReleaseService _releaseService;

    public BulkAssignmentService(FleetDbContext db, DeviceTagService tagService, ReleaseService releaseService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _tagService = tagService ?? throw new ArgumentNullException(nameof(tagService));
        _releaseService = releaseService ?? throw new ArgumentNullException(nameof(releaseService));
    }

    /// <summary>
    /// Assign a deployment to all devices with any of the specified tags.
    /// Uses OR logic: device must have at least one of the tags.
    /// </summary>
    /// <returns>BulkAssignmentResult with counts of successful/failed assignments</returns>
    public async Task<BulkAssignmentResult> AssignToDevicesWithAnyTagAsync(
        Guid deploymentId,
        IEnumerable<string> tagNames,
        CancellationToken ct = default)
    {
        if (deploymentId == Guid.Empty)
            throw new ArgumentException("Deployment ID required", nameof(deploymentId));

        var tags = tagNames.ToList();
        if (!tags.Any())
            throw new ArgumentException("At least one tag name required", nameof(tagNames));

        // Verify deployment exists
        var deployment = await _releaseService.GetDeploymentAsync(deploymentId);
        if (deployment == null)
            throw new KeyNotFoundException($"Deployment {deploymentId} not found");
        if (!deployment.IsActive)
            throw new InvalidOperationException("Deployment is not active");

        // Resolve tag names to tag IDs
        var resolvedTags = new List<Guid>();
        foreach (var tagName in tags)
        {
            var tag = await _tagService.GetTagByNameAsync(tagName, ct);
            if (tag == null)
                throw new KeyNotFoundException($"Tag '{tagName}' not found");
            resolvedTags.Add(tag.TagId);
        }

        // Get devices with any of the tags
        var devices = await _tagService.GetDevicesWithAnyTagAsync(resolvedTags, ct);

        return await AssignToDevicesInternalAsync(deploymentId, devices, ct);
    }

    /// <summary>
    /// Assign a deployment to all devices with all of the specified tags.
    /// Uses AND logic: device must have every tag in the list.
    /// </summary>
    /// <returns>BulkAssignmentResult with counts of successful/failed assignments</returns>
    public async Task<BulkAssignmentResult> AssignToDevicesWithAllTagsAsync(
        Guid deploymentId,
        IEnumerable<string> tagNames,
        CancellationToken ct = default)
    {
        if (deploymentId == Guid.Empty)
            throw new ArgumentException("Deployment ID required", nameof(deploymentId));

        var tags = tagNames.ToList();
        if (!tags.Any())
            throw new ArgumentException("At least one tag name required", nameof(tagNames));

        // Verify deployment exists
        var deployment = await _releaseService.GetDeploymentAsync(deploymentId);
        if (deployment == null)
            throw new KeyNotFoundException($"Deployment {deploymentId} not found");
        if (!deployment.IsActive)
            throw new InvalidOperationException("Deployment is not active");

        // Resolve tag names to tag IDs
        var resolvedTags = new List<Guid>();
        foreach (var tagName in tags)
        {
            var tag = await _tagService.GetTagByNameAsync(tagName, ct);
            if (tag == null)
                throw new KeyNotFoundException($"Tag '{tagName}' not found");
            resolvedTags.Add(tag.TagId);
        }

        // Get devices with all tags
        var devices = await _tagService.GetDevicesWithAllTagsAsync(resolvedTags, ct);

        return await AssignToDevicesInternalAsync(deploymentId, devices, ct);
    }

    /// <summary>
    /// Assign a deployment to a specific list of devices (bulk direct assignment).
    /// Useful for explicitly selected device groups.
    /// </summary>
    /// <returns>BulkAssignmentResult with counts of successful/failed assignments</returns>
    public async Task<BulkAssignmentResult> AssignToDevicesAsync(
        Guid deploymentId,
        IEnumerable<Guid> deviceIds,
        CancellationToken ct = default)
    {
        if (deploymentId == Guid.Empty)
            throw new ArgumentException("Deployment ID required", nameof(deploymentId));

        var devices = deviceIds.ToList();
        if (!devices.Any())
            throw new ArgumentException("At least one device ID required", nameof(deviceIds));

        // Verify deployment exists
        var deployment = await _releaseService.GetDeploymentAsync(deploymentId);
        if (deployment == null)
            throw new KeyNotFoundException($"Deployment {deploymentId} not found");
        if (!deployment.IsActive)
            throw new InvalidOperationException("Deployment is not active");

        // Load actual device objects
        var deviceObjects = await _db.Devices
            .Where(d => devices.Contains(d.DeviceId))
            .ToListAsync(ct);

        if (!deviceObjects.Any())
            throw new ArgumentException("No valid devices found", nameof(deviceIds));

        return await AssignToDevicesInternalAsync(deploymentId, deviceObjects, ct);
    }

    /// <summary>
    /// Get the status of a bulk assignment operation.
    /// Returns counts of assigned, failed, and pending assignments.
    /// </summary>
    public async Task<BulkAssignmentStatusSnapshot> GetBulkAssignmentStatusAsync(
        Guid deploymentId,
        CancellationToken ct = default)
    {
        if (deploymentId == Guid.Empty)
            throw new ArgumentException("Deployment ID required", nameof(deploymentId));

        var deployment = await _releaseService.GetDeploymentAsync(deploymentId);
        if (deployment == null)
            throw new KeyNotFoundException($"Deployment {deploymentId} not found");

        // Get all device states for this deployment
        var states = await _db.DeploymentDeviceStates
            .Where(s => s.DeploymentId == deploymentId)
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return new BulkAssignmentStatusSnapshot
        {
            DeploymentId = deploymentId,
            Total = states.Sum(s => s.Count),
            Pending = states.FirstOrDefault(s => s.Status == DeploymentDeviceStatus.Pending)?.Count ?? 0,
            Downloading = states.FirstOrDefault(s => s.Status == DeploymentDeviceStatus.Downloading)?.Count ?? 0,
            Installing = states.FirstOrDefault(s => s.Status == DeploymentDeviceStatus.Installing)?.Count ?? 0,
            Completed = states.FirstOrDefault(s => s.Status == DeploymentDeviceStatus.Completed)?.Count ?? 0,
            Failed = states.FirstOrDefault(s => s.Status == DeploymentDeviceStatus.Failed)?.Count ?? 0,
            QueryTime = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Get assignment results for a specific device group query (for troubleshooting).
    /// Returns the actual devices that would be targeted by tag criteria.
    /// </summary>
    public async Task<List<Device>> PreviewAssignmentTargetsAsync(
        IEnumerable<string> tagNames,
        bool useAllLogic = false,
        CancellationToken ct = default)
    {
        var tags = tagNames.ToList();
        if (!tags.Any())
            throw new ArgumentException("At least one tag name required", nameof(tagNames));

        // Resolve tag names to tag IDs
        var resolvedTags = new List<Guid>();
        foreach (var tagName in tags)
        {
            var tag = await _tagService.GetTagByNameAsync(tagName, ct);
            if (tag == null)
                throw new KeyNotFoundException($"Tag '{tagName}' not found");
            resolvedTags.Add(tag.TagId);
        }

        // Get devices with specified logic
        var devices = useAllLogic
            ? await _tagService.GetDevicesWithAllTagsAsync(resolvedTags, ct)
            : await _tagService.GetDevicesWithAnyTagAsync(resolvedTags, ct);

        return devices;
    }

    private async Task<BulkAssignmentResult> AssignToDevicesInternalAsync(
        Guid deploymentId,
        List<Device> devices,
        CancellationToken ct)
    {
        var result = new BulkAssignmentResult
        {
            DeploymentId = deploymentId,
            RequestedCount = devices.Count,
            StartTime = DateTimeOffset.UtcNow
        };

        try
        {
            // Get device enrollments for batch assignment
            var deviceIds = devices.Select(d => d.DeviceId).ToList();
            var enrollments = await _db.PendingEnrollments
                .Where(e => deviceIds.Contains(e.DeviceId!.Value))
                .ToListAsync(ct);

            var enrollmentMap = enrollments.ToDictionary(e => e.DeviceId!.Value);

            // Assign deployment to each device
            foreach (var device in devices)
            {
                try
                {
                    if (!enrollmentMap.TryGetValue(device.DeviceId, out var enrollment))
                    {
                        result.FailureCount++;
                        result.FailureReasons.Add($"Device {device.DeviceId}: Enrollment not found");
                        continue;
                    }

                    // This is idempotent - will return existing state if already assigned
                    await _releaseService.AssignDeploymentAsync(deploymentId, enrollment.Id);
                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    result.FailureCount++;
                    result.FailureReasons.Add($"Device {device.DeviceId}: {ex.Message}");
                }
            }

            result.EndTime = DateTimeOffset.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            result.Status = BulkAssignmentStatusEnum.Success;
        }
        catch (Exception ex)
        {
            result.Status = BulkAssignmentStatusEnum.Failed;
            result.FailureReasons.Add($"Bulk operation failed: {ex.Message}");
            result.EndTime = DateTimeOffset.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
        }

        return result;
    }
}

/// <summary>
/// Result of a bulk assignment operation.
/// </summary>
public class BulkAssignmentResult
{
    /// <summary>Deployment ID that was assigned.</summary>
    public Guid DeploymentId { get; set; }

    /// <summary>Number of devices requested for assignment.</summary>
    public int RequestedCount { get; set; }

    /// <summary>Number of successful assignments.</summary>
    public int SuccessCount { get; set; }

    /// <summary>Number of failed assignments.</summary>
    public int FailureCount { get; set; }

    /// <summary>Overall operation status.</summary>
    public BulkAssignmentStatusEnum Status { get; set; }

    /// <summary>Detailed failure reasons.</summary>
    public List<string> FailureReasons { get; } = new();

    /// <summary>Operation start time.</summary>
    public DateTimeOffset StartTime { get; set; }

    /// <summary>Operation end time.</summary>
    public DateTimeOffset EndTime { get; set; }

    /// <summary>Total operation duration.</summary>
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Status enum for bulk assignment operations.
/// </summary>
public enum BulkAssignmentStatusEnum
{
    Success = 0,
    PartialSuccess = 1,
    Failed = 2
}

/// <summary>
/// Current status snapshot of a bulk assignment (deployment).
/// Tracks how many devices are in each state.
/// </summary>
public class BulkAssignmentStatusSnapshot
{
    /// <summary>Deployment ID.</summary>
    public Guid DeploymentId { get; set; }

    /// <summary>Total devices assigned to this deployment.</summary>
    public int Total { get; set; }

    /// <summary>Devices pending acknowledgement.</summary>
    public int Pending { get; set; }

    /// <summary>Devices currently downloading the APK.</summary>
    public int Downloading { get; set; }

    /// <summary>Devices currently installing the APK.</summary>
    public int Installing { get; set; }

    /// <summary>Devices that have completed installation.</summary>
    public int Completed { get; set; }

    /// <summary>Devices that failed the deployment.</summary>
    public int Failed { get; set; }

    /// <summary>Completion percentage (Completed + Failed) / Total.</summary>
    public double CompletionPercentage => Total == 0 ? 0 : ((Completed + Failed) / (double)Total) * 100;

    /// <summary>Success percentage Completed / Total.</summary>
    public double SuccessPercentage => Total == 0 ? 0 : (Completed / (double)Total) * 100;

    /// <summary>In-progress count (Downloading + Installing).</summary>
    public int InProgress => Downloading + Installing;

    /// <summary>When this status was queried.</summary>
    public DateTimeOffset QueryTime { get; set; }
}

/// <summary>
/// Current status snapshot of a bulk assignment (deployment).
/// Tracks how many devices are in each state.
/// </summary>
public class BulkAssignmentStatus
{
    /// <summary>Deployment ID.</summary>
    public Guid DeploymentId { get; set; }

    /// <summary>Total devices assigned to this deployment.</summary>
    public int Total { get; set; }

    /// <summary>Devices that have acknowledged the deployment.</summary>
    public int Acknowledged { get; set; }

    /// <summary>Devices currently downloading the APK.</summary>
    public int Downloading { get; set; }

    /// <summary>Devices currently installing the APK.</summary>
    public int Installing { get; set; }

    /// <summary>Devices that have completed installation.</summary>
    public int Completed { get; set; }

    /// <summary>Devices that failed the deployment.</summary>
    public int Failed { get; set; }

    /// <summary>Completion percentage (Completed + Failed) / Total.</summary>
    public double CompletionPercentage => Total == 0 ? 0 : ((Completed + Failed) / (double)Total) * 100;

    /// <summary>Success percentage Completed / Total.</summary>
    public double SuccessPercentage => Total == 0 ? 0 : (Completed / (double)Total) * 100;

    /// <summary>In-progress count (Downloading + Installing).</summary>
    public int InProgress => Downloading + Installing;

    /// <summary>When this status was queried.</summary>
    public DateTimeOffset QueryTime { get; set; }
}
