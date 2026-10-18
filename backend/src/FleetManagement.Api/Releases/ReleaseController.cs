using FleetManagement.Releases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    public ReleaseController(ReleaseService releaseService)
    {
        _releaseService = releaseService;
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
