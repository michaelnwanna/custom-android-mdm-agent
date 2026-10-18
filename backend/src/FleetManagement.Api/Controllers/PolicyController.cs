namespace FleetManagement.Api.Controllers;

using System;
using System.Threading.Tasks;
using FleetManagement.Policies;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Requests and responses for policy management API.
/// </summary>

/// <summary>
/// Request to create a policy.
/// </summary>
public sealed record CreatePolicyRequest
{
    /// <summary>
    /// Compact JSON policy document (must be valid JSON object, max 64 KB).
    /// This content hash determines the policy ID, ensuring idempotent creation.
    /// </summary>
    public required string PolicyJson { get; init; }
}

/// <summary>
/// Response from policy creation.
/// </summary>
public sealed record CreatePolicyResponse
{
    /// <summary>
    /// The policy ID (UUID derived from JSON content hash).
    /// Same PolicyJson always produces the same ID.
    /// </summary>
    public required Guid PolicyId { get; init; }

    /// <summary>
    /// Policy version (incremented on updates, starts at 1).
    /// </summary>
    public required long Version { get; init; }

    /// <summary>
    /// When the policy was created (UTC).
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Request to assign a policy to a device.
/// </summary>
public sealed record AssignPolicyRequest
{
    /// <summary>
    /// Enrolled device ID.
    /// </summary>
    public required Guid EnrollmentId { get; init; }

    /// <summary>
    /// Policy ID to assign.
    /// </summary>
    public required Guid PolicyId { get; init; }
}

/// <summary>
/// Response from policy assignment.
/// </summary>
public sealed record AssignPolicyResponse
{
    /// <summary>
    /// The policy assignment ID for tracking.
    /// </summary>
    public required Guid AssignmentId { get; init; }

    /// <summary>
    /// The policy version that was assigned.
    /// </summary>
    public required long AssignedVersion { get; init; }

    /// <summary>
    /// When the assignment was created (UTC).
    /// </summary>
    public required DateTimeOffset AssignedAt { get; init; }
}

/// <summary>
/// Policy management REST API.
/// </summary>
[ApiController]
[Route("api/v1/policies")]
public sealed class PolicyController : ControllerBase
{
    private readonly PolicyService _service;

    public PolicyController(PolicyService service)
    {
        _service = service;
    }

    /// <summary>
    /// Create or retrieve a policy. Idempotent.
    /// Same PolicyJson produces the same policy ID.
    /// </summary>
    /// <param name="request">Policy creation request</param>
    /// <returns>Policy ID, version, and creation timestamp</returns>
    /// <response code="200">Policy created or retrieved</response>
    /// <response code="400">Invalid JSON or exceeds 64 KB</response>
    /// <response code="401">Unauthorized; missing or invalid bearer token</response>
    [HttpPost("create")]
    [ProducesResponseType(typeof(CreatePolicyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CreatePolicyResponse>> Create([FromBody] CreatePolicyRequest request)
    {
        try
        {
            var creator = User.FindFirst("sub")?.Value ?? "unknown";
            var policyId = await _service.CreatePolicyIdempotentAsync(request.PolicyJson, creator);
            var policy = await _service.GetPolicyAsync(policyId);

            if (policy == null)
                return BadRequest(new { error = "Policy creation failed" });

            return Ok(new CreatePolicyResponse
            {
                PolicyId = policy.PolicyId,
                Version = policy.CurrentVersion,
                CreatedAt = policy.CreatedAt
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Assign a policy to an enrolled device.
    /// </summary>
    /// <param name="request">Policy assignment request</param>
    /// <returns>Assignment ID, version, and timestamp</returns>
    /// <response code="200">Policy assigned</response>
    /// <response code="400">Device not enrolled or policy not found</response>
    /// <response code="401">Unauthorized</response>
    [HttpPost("assign")]
    [ProducesResponseType(typeof(AssignPolicyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AssignPolicyResponse>> Assign([FromBody] AssignPolicyRequest request)
    {
        try
        {
            var assignmentId = await _service.AssignPolicyAsync(request.EnrollmentId, request.PolicyId);
            var assignment = await _service.GetAssignmentAsync(assignmentId);

            if (assignment == null)
                return BadRequest(new { error = "Assignment failed" });

            return Ok(new AssignPolicyResponse
            {
                AssignmentId = assignment.AssignmentId,
                AssignedVersion = assignment.AssignedVersion,
                AssignedAt = assignment.AssignedAt
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
