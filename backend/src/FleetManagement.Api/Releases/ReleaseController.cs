using FleetManagement.Api.Mqtt;
using FleetManagement.Persistence;
using FleetManagement.Releases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FleetManagement.Api.Releases;

/// <summary>
/// REST API controller for release management.
/// Mirrors PolicyController patterns for consistency.
/// </summary>
[ApiController]
[Route("api/v1/releases")]
[Authorize(Policy = "AdminOnly")]
public class ReleaseController : ControllerBase
{
    private readonly ReleaseService _releaseService;
    private readonly ICommandPublisher? _commandPublisher;
    private readonly IDbContextFactory<FleetDbContext> _dbContextFactory;

    public ReleaseController(
        ReleaseService releaseService,
        ICommandPublisher? commandPublisher,
        IDbContextFactory<FleetDbContext> dbContextFactory)
    {
        _releaseService = releaseService;
        _commandPublisher = commandPublisher;
        _dbContextFactory = dbContextFactory;
    }

    /// <summary>
    /// Create a new application release.
    /// Idempotent: same metadata produces same release ID.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreateReleaseResponse>> CreateReleaseAsync(
        [FromBody] CreateReleaseRequest request)
    {
        if (request == null)
            return BadRequest("Request cannot be null");

        if (string.IsNullOrWhiteSpace(request.MetadataJson))
            return BadRequest("MetadataJson is required");

        if (string.IsNullOrWhiteSpace(request.MetadataSignature))
            return BadRequest("MetadataSignature is required");

        try
        {
            var releaseId = await _releaseService.CreateReleaseIdempotentAsync(
                metadataJson: request.MetadataJson,
                metadataSignature: request.MetadataSignature,
                creator: User.Identity?.Name ?? "system"
            );

            return Ok(new CreateReleaseResponse
            {
                ReleaseId = releaseId.ToString(),
                IsNew = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get release by ID.
    /// </summary>
    [HttpGet("{releaseId}")]
    public async Task<ActionResult<GetReleaseResponse>> GetReleaseAsync(
        [FromRoute] string releaseId)
    {
        if (string.IsNullOrWhiteSpace(releaseId) || !Guid.TryParse(releaseId, out var releaseGuid))
            return BadRequest("Valid ReleaseId is required");

        var release = await _releaseService.GetReleaseAsync(releaseGuid);
        if (release == null)
            return NotFound();

        return Ok(new GetReleaseResponse
        {
            ReleaseId = release.ReleaseId.ToString(),
            MetadataJson = release.MetadataJson,
            MetadataSignature = release.MetadataSignature,
            IsActive = release.IsActive,
            CreatedAt = release.CreatedAt,
            CreatedBy = release.CreatedBy
        });
    }

    /// <summary>
    /// Create a pilot deployment for a release.
    /// Pilot stage is for initial testing with a limited set of devices.
    /// </summary>
    [HttpPost("{releaseId}/deployments/pilot")]
    public async Task<ActionResult<CreateDeploymentResponse>> CreatePilotDeploymentAsync(
        [FromRoute] string releaseId)
    {
        if (string.IsNullOrWhiteSpace(releaseId) || !Guid.TryParse(releaseId, out var releaseGuid))
            return BadRequest("Valid ReleaseId is required");

        try
        {
            var deploymentId = await _releaseService.CreatePilotDeploymentAsync(
                releaseId: releaseGuid,
                creator: User.Identity?.Name ?? "system"
            );

            var deployment = await _releaseService.GetDeploymentAsync(deploymentId);
            return Ok(new CreateDeploymentResponse
            {
                DeploymentId = deploymentId.ToString(),
                ReleaseId = releaseGuid.ToString(),
                Stage = "Pilot",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Create a staged deployment for a release.
    /// Staged deployments roll out to a percentage of devices.
    /// </summary>
    [HttpPost("{releaseId}/deployments/staged")]
    public async Task<ActionResult<CreateDeploymentResponse>> CreateStagedDeploymentAsync(
        [FromRoute] string releaseId,
        [FromBody] CreateStagedDeploymentRequest request)
    {
        if (string.IsNullOrWhiteSpace(releaseId) || !Guid.TryParse(releaseId, out var releaseGuid))
            return BadRequest("Valid ReleaseId is required");

        if (request == null)
            return BadRequest("Request cannot be null");

        if (request.RolloutPercentage <= 0 || request.RolloutPercentage > 100)
            return BadRequest("RolloutPercentage must be 1-100");

        try
        {
            var deploymentId = await _releaseService.CreateStagedDeploymentAsync(
                releaseId: releaseGuid,
                rolloutPercentage: request.RolloutPercentage,
                creator: User.Identity?.Name ?? "system"
            );

            return Ok(new CreateDeploymentResponse
            {
                DeploymentId = deploymentId.ToString(),
                ReleaseId = releaseGuid.ToString(),
                Stage = "Staged",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get deployment by ID.
    /// </summary>
    [HttpGet("deployments/{deploymentId}")]
    public async Task<ActionResult<GetDeploymentResponse>> GetDeploymentAsync(
        [FromRoute] string deploymentId)
    {
        if (string.IsNullOrWhiteSpace(deploymentId) || !Guid.TryParse(deploymentId, out var deploymentGuid))
            return BadRequest("Valid DeploymentId is required");

        var deployment = await _releaseService.GetDeploymentAsync(deploymentGuid);
        if (deployment == null)
            return NotFound();

        return Ok(new GetDeploymentResponse
        {
            DeploymentId = deployment.DeploymentId.ToString(),
            ReleaseId = deployment.ReleaseId.ToString(),
            Stage = deployment.Stage.ToString(),
            RolloutPercentage = deployment.RolloutPercentage,
            CreatedAt = deployment.CreatedAt,
            CreatedBy = deployment.CreatedBy
        });
    }

    /// <summary>
    /// Promote a staged deployment to production.
    /// Idempotent: promoting an already-promoted deployment returns success.
    /// </summary>
    [HttpPost("deployments/{deploymentId}/promote")]
    public async Task<ActionResult<PromoteDeploymentResponse>> PromoteDeploymentAsync(
        [FromRoute] string deploymentId)
    {
        if (string.IsNullOrWhiteSpace(deploymentId) || !Guid.TryParse(deploymentId, out var deploymentGuid))
            return BadRequest("Valid DeploymentId is required");

        try
        {
            await _releaseService.PromoteStagedDeploymentAsync(deploymentGuid);
            var promoted = await _releaseService.GetDeploymentAsync(deploymentGuid);

            // Publish INSTALL_RELEASE commands to all assigned devices via MQTT
            if (_commandPublisher != null)
            {
                using var db = _dbContextFactory.CreateDbContext();
                var assignedDevices = await db.DeploymentDeviceStates
                    .Where(s => s.DeploymentId == deploymentGuid)
                    .Select(s => new { s.DeploymentId, s.EnrollmentId })
                    .ToListAsync();

                foreach (var device in assignedDevices)
                {
                    try
                    {
                        var release = await _releaseService.GetReleaseAsync(promoted!.ReleaseId);
                        if (release != null)
                        {
                            await _commandPublisher.PublishInstallReleaseAsync(
                                device.EnrollmentId,
                                release.ReleaseId
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log but don't fail promotion if MQTT publishing fails
                        // The device state can be published later via retry or manual intervention
                        // For now, just log the error
                        System.Diagnostics.Debug.WriteLine($"Failed to publish MQTT command for device {device.EnrollmentId}: {ex.Message}");
                    }
                }
            }

            return Ok(new PromoteDeploymentResponse
            {
                DeploymentId = promoted!.DeploymentId.ToString(),
                Stage = promoted.Stage.ToString(),
                RolloutPercentage = promoted.RolloutPercentage
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get device deployment state for a specific device.
    /// </summary>
    [HttpGet("deployments/{deploymentId}/devices/{enrollmentId}")]
    public async Task<ActionResult<GetDeviceStateResponse>> GetDeviceStateAsync(
        [FromRoute] string deploymentId,
        [FromRoute] string enrollmentId)
    {
        if (string.IsNullOrWhiteSpace(deploymentId) || !Guid.TryParse(deploymentId, out var deploymentGuid))
            return BadRequest("Valid DeploymentId is required");

        if (string.IsNullOrWhiteSpace(enrollmentId) || !Guid.TryParse(enrollmentId, out var enrollmentGuid))
            return BadRequest("Valid EnrollmentId is required");

        var deviceState = await _releaseService.GetDeviceDeploymentStateAsync(deploymentGuid, enrollmentGuid);
        if (deviceState == null)
            return NotFound();

        return Ok(new GetDeviceStateResponse
        {
            DeploymentId = deviceState.DeploymentId.ToString(),
            EnrollmentId = deviceState.EnrollmentId.ToString(),
            Status = deviceState.Status.ToString(),
            ErrorMessage = deviceState.ErrorMessage,
            CreatedAt = deviceState.CreatedAt,
            UpdatedAt = deviceState.UpdatedAt
        });
    }

    /// <summary>
    /// Update device deployment state as it progresses through install.
    /// Devices call this endpoint to report: Downloading, Installing, Completed, or Failed.
    /// </summary>
    [HttpPut("deployments/{deploymentId}/devices/{enrollmentId}/status")]
    [AllowAnonymous]
    public async Task<ActionResult> UpdateDeviceStatusAsync(
        [FromRoute] string deploymentId,
        [FromRoute] string enrollmentId,
        [FromBody] UpdateDeviceStatusRequest request)
    {
        if (string.IsNullOrWhiteSpace(deploymentId) || !Guid.TryParse(deploymentId, out var deploymentGuid))
            return BadRequest("Valid DeploymentId is required");

        if (string.IsNullOrWhiteSpace(enrollmentId) || !Guid.TryParse(enrollmentId, out var enrollmentGuid))
            return BadRequest("Valid EnrollmentId is required");

        if (request == null)
            return BadRequest("Request cannot be null");

        if (string.IsNullOrWhiteSpace(request.Status))
            return BadRequest("Status is required");

        // Parse status
        if (!Enum.TryParse<DeploymentDeviceStatus>(request.Status, ignoreCase: true, out var status))
            return BadRequest($"Invalid status. Must be one of: {string.Join(", ", Enum.GetNames(typeof(DeploymentDeviceStatus)))}");

        try
        {
            var updated = await _releaseService.UpdateDeviceDeploymentStateAsync(
                deploymentGuid,
                enrollmentGuid,
                status,
                request.ErrorMessage
            );

            if (updated == null)
                return NotFound(new { error = "Device deployment state not found" });

            return Ok(new UpdateDeviceStatusResponse
            {
                DeploymentId = updated.DeploymentId.ToString(),
                EnrollmentId = updated.EnrollmentId.ToString(),
                Status = updated.Status.ToString(),
                UpdatedAt = updated.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Assign a deployment to an enrolled device.
    /// Creates device state tracking and publishes INSTALL_RELEASE command via MQTT if available.
    /// </summary>
    [HttpPost("{deploymentId}/assign")]
    public async Task<ActionResult<AssignDeploymentResponse>> AssignDeploymentAsync(
        [FromRoute] string deploymentId,
        [FromBody] AssignDeploymentRequest request)
    {
        if (string.IsNullOrWhiteSpace(deploymentId) || !Guid.TryParse(deploymentId, out var deploymentGuid))
            return BadRequest("Valid DeploymentId is required");

        if (request == null)
            return BadRequest("Request cannot be null");

        if (string.IsNullOrWhiteSpace(request.EnrollmentId) || !Guid.TryParse(request.EnrollmentId, out var enrollmentGuid))
            return BadRequest("Valid EnrollmentId is required");

        try
        {
            // Perform the assignment in the domain
            var stateId = await _releaseService.AssignDeploymentAsync(deploymentGuid, enrollmentGuid);

            // If MQTT is available, publish the command
            if (_commandPublisher != null)
            {
                try
                {
                    // Get the deployment to find the release ID and device ID
                    var deployment = await _releaseService.GetDeploymentAsync(deploymentGuid);
                    if (deployment != null)
                    {
                        using var db = _dbContextFactory.CreateDbContext();
                        var enrollment = await db.Set<FleetManagement.Enrollment.PendingEnrollment>()
                            .FirstOrDefaultAsync(e => e.Id == enrollmentGuid);

                        if (enrollment?.DeviceId.HasValue == true)
                        {
                            await _commandPublisher.PublishInstallReleaseAsync(
                                enrollment.DeviceId.Value,
                                deployment.ReleaseId
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log MQTT error but don't fail the assignment
                    System.Console.WriteLine($"MQTT publish failed: {ex.Message}");
                }
            }

            return Ok(new AssignDeploymentResponse
            {
                StateId = stateId.ToString(),
                DeploymentId = deploymentId,
                EnrollmentId = request.EnrollmentId,
                Status = "Pending",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get deployment progress analytics.
    /// Returns device count breakdown by status, completion percentage, and success rate.
    /// </summary>
    [HttpGet("deployments/{deploymentId}/progress")]
    [AllowAnonymous]
    public async Task<ActionResult<DeploymentProgressResponse>> GetDeploymentProgressAsync(
        [FromRoute] string deploymentId)
    {
        if (string.IsNullOrWhiteSpace(deploymentId) || !Guid.TryParse(deploymentId, out var deploymentGuid))
            return BadRequest("Valid DeploymentId is required");

        var analyticsService = new DeploymentAnalyticsService(_dbContextFactory);
        var analytics = await analyticsService.GetDeploymentProgressAsync(deploymentGuid);

        if (analytics == null)
            return NotFound();

        return Ok(MapToResponse(analytics));
    }

    /// <summary>
    /// Get release progress analytics.
    /// Returns per-deployment breakdown plus aggregated totals.
    /// </summary>
    [HttpGet("{releaseId}/progress")]
    [AllowAnonymous]
    public async Task<ActionResult<ReleaseProgressResponse>> GetReleaseProgressAsync(
        [FromRoute] string releaseId)
    {
        if (string.IsNullOrWhiteSpace(releaseId) || !Guid.TryParse(releaseId, out var releaseGuid))
            return BadRequest("Valid ReleaseId is required");

        var analyticsService = new DeploymentAnalyticsService(_dbContextFactory);
        var analytics = await analyticsService.GetReleaseProgressAsync(releaseGuid);

        if (analytics == null)
            return NotFound();

        return Ok(MapToResponse(analytics));
    }

    /// <summary>
    /// Map internal DeploymentProgressAnalytics to API response DTO.
    /// </summary>
    private static DeploymentProgressResponse MapToResponse(DeploymentProgressAnalytics analytics)
    {
        return new DeploymentProgressResponse
        {
            DeploymentId = analytics.DeploymentId.ToString(),
            ReleaseId = analytics.ReleaseId.ToString(),
            Stage = analytics.Stage,
            TotalDevices = analytics.TotalDevices,
            Counts = new StatusCountsResponse
            {
                Pending = analytics.Counts.Pending,
                Downloading = analytics.Counts.Downloading,
                Installing = analytics.Counts.Installing,
                Completed = analytics.Counts.Completed,
                Failed = analytics.Counts.Failed
            },
            CompletionPercentage = analytics.Counts.CompletionPercentage,
            SuccessRate = analytics.Counts.SuccessRate,
            FailureRate = analytics.Counts.FailureRate,
            InProgress = analytics.Counts.InProgress,
            LastUpdatedAt = analytics.LastUpdatedAt
        };
    }

    /// <summary>
    /// Map internal ReleaseProgressAnalytics to API response DTO.
    /// </summary>
    private static ReleaseProgressResponse MapToResponse(ReleaseProgressAnalytics analytics)
    {
        return new ReleaseProgressResponse
        {
            ReleaseId = analytics.ReleaseId.ToString(),
            PackageName = analytics.PackageName,
            VersionCode = analytics.VersionCode,
            Deployments = analytics.Deployments.Select(MapToResponse).ToList(),
            Totals = new StatusCountsResponse
            {
                Pending = analytics.Totals.Pending,
                Downloading = analytics.Totals.Downloading,
                Installing = analytics.Totals.Installing,
                Completed = analytics.Totals.Completed,
                Failed = analytics.Totals.Failed
            },
            TotalCompletionPercentage = analytics.Totals.CompletionPercentage,
            TotalSuccessRate = analytics.Totals.SuccessRate,
            TotalFailureRate = analytics.Totals.FailureRate,
            TotalInProgress = analytics.Totals.InProgress
        };
    }
}

/// <summary>
/// Request and Response DTOs for Release endpoints.
/// Mirrors PolicyController DTO patterns.
/// </summary>

public class CreateReleaseRequest
{
    public string MetadataJson { get; set; } = null!;
    public string MetadataSignature { get; set; } = null!;
}

public class CreateReleaseResponse
{
    public string ReleaseId { get; set; } = null!;
    public bool IsNew { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class GetReleaseResponse
{
    public string ReleaseId { get; set; } = null!;
    public string MetadataJson { get; set; } = null!;
    public string MetadataSignature { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
}

public class CreateStagedDeploymentRequest
{
    public int RolloutPercentage { get; set; }
}

public class CreateDeploymentResponse
{
    public string DeploymentId { get; set; } = null!;
    public string ReleaseId { get; set; } = null!;
    public string Stage { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}

public class GetDeploymentResponse
{
    public string DeploymentId { get; set; } = null!;
    public string ReleaseId { get; set; } = null!;
    public string Stage { get; set; } = null!;
    public int RolloutPercentage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = null!;
}

public class PromoteDeploymentResponse
{
    public string DeploymentId { get; set; } = null!;
    public string Stage { get; set; } = null!;
    public int RolloutPercentage { get; set; }
}

public class GetDeviceStateResponse
{
    public string DeploymentId { get; set; } = null!;
    public string EnrollmentId { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class UpdateDeviceStatusRequest
{
    /// <summary>
    /// New status: Downloading, Installing, Completed, or Failed.
    /// </summary>
    public string Status { get; set; } = null!;

    /// <summary>
    /// Optional error message for Failed status.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

public class UpdateDeviceStatusResponse
{
    public string DeploymentId { get; set; } = null!;
    public string EnrollmentId { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset UpdatedAt { get; set; }
}

public class AssignDeploymentRequest
{
    /// <summary>
    /// Enrollment ID to assign the deployment to.
    /// </summary>
    public string EnrollmentId { get; set; } = null!;
}

public class AssignDeploymentResponse
{
    /// <summary>
    /// The device deployment state ID.
    /// </summary>
    public string StateId { get; set; } = null!;

    /// <summary>
    /// The deployment that was assigned.
    /// </summary>
    public string DeploymentId { get; set; } = null!;

    /// <summary>
    /// The enrollment (device) assigned.
    /// </summary>
    public string EnrollmentId { get; set; } = null!;

    /// <summary>
    /// Initial status (always Pending for new assignments).
    /// </summary>
    public string Status { get; set; } = null!;

    /// <summary>
    /// When the assignment was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Status count breakdown response.
/// </summary>
public class StatusCountsResponse
{
    public int Pending { get; set; }
    public int Downloading { get; set; }
    public int Installing { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
}

/// <summary>
/// Deployment progress analytics response.
/// </summary>
public class DeploymentProgressResponse
{
    public string DeploymentId { get; set; } = null!;
    public string ReleaseId { get; set; } = null!;
    public string Stage { get; set; } = null!;
    public int TotalDevices { get; set; }
    public StatusCountsResponse Counts { get; set; } = null!;
    public int CompletionPercentage { get; set; }
    public int SuccessRate { get; set; }
    public int FailureRate { get; set; }
    public int InProgress { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
}

/// <summary>
/// Release progress analytics response.
/// Includes all deployments and aggregated totals.
/// </summary>
public class ReleaseProgressResponse
{
    public string ReleaseId { get; set; } = null!;
    public string PackageName { get; set; } = null!;
    public long VersionCode { get; set; }
    public List<DeploymentProgressResponse> Deployments { get; set; } = new();
    public StatusCountsResponse Totals { get; set; } = null!;
    public int TotalCompletionPercentage { get; set; }
    public int TotalSuccessRate { get; set; }
    public int TotalFailureRate { get; set; }
    public int TotalInProgress { get; set; }
}
