namespace FleetManagement.Api.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FleetManagement.Devices;
using FleetManagement.Persistence;
using FleetManagement.Policies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    private readonly PolicyCommandGenerator _commandGenerator;
    private readonly FleetDbContext _db;

    public PolicyController(
        PolicyService service,
        PolicyCommandGenerator commandGenerator,
        FleetDbContext db)
    {
        _service = service;
        _commandGenerator = commandGenerator;
        _db = db;
    }

    /// <summary>
    /// List stored policies for the management console.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ManagedPoliciesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ManagedPoliciesResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string? search = null,
        [FromQuery] string? type = null,
        [FromQuery] string? status = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null)
    {
        if (page < 1 || pageSize is < 1 or > 500)
            return BadRequest(new { error = "Page must be positive and pageSize must be between 1 and 500." });

        var policies = await _db.Policies.AsNoTracking()
            .OrderByDescending(policy => policy.ModifiedAt)
            .ToListAsync();
        var assignmentCounts = await GetCurrentPolicyAssignmentCountsAsync();

        var results = policies.Select(policy =>
        {
            var metadata = ReadPolicyMetadata(policy.PolicyJson, policy.PolicyId);
            assignmentCounts.TryGetValue(policy.PolicyId, out var counts);
            return new ManagedPolicyResponse
            {
                PolicyId = policy.PolicyId,
                Name = metadata.Name,
                Description = metadata.Description,
                Type = metadata.Type,
                Status = policy.IsActive ? "Active" : "Archived",
                Version = policy.CurrentVersion,
                DeviceCount = counts.Total,
                CompliantDeviceCount = counts.Compliant,
                CompliancePercentage = counts.Total > 0
                    ? (int)Math.Round(100d * counts.Compliant / counts.Total)
                    : 0,
                Configuration = metadata.Configuration,
                CreatedAt = policy.CreatedAt,
                UpdatedAt = policy.ModifiedAt
            };
        });

        if (!string.IsNullOrWhiteSpace(search))
        {
            results = results.Where(policy =>
                policy.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                policy.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(type))
            results = results.Where(policy => string.Equals(policy.Type, type, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(status))
            results = results.Where(policy => string.Equals(policy.Status, status, StringComparison.OrdinalIgnoreCase));
        results = string.Equals(sortBy, "name", StringComparison.OrdinalIgnoreCase)
            ? string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase)
                ? results.OrderBy(policy => policy.Name)
                : results.OrderByDescending(policy => policy.Name)
            : string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase)
                ? results.OrderBy(policy => policy.UpdatedAt)
                : results.OrderByDescending(policy => policy.UpdatedAt);

        var filtered = results.ToList();
        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return Ok(new ManagedPoliciesResponse
        {
            Items = items,
            TotalCount = filtered.Count,
            PageNumber = page,
            PageSize = pageSize,
            TotalPages = (filtered.Count + pageSize - 1) / pageSize
        });
    }

    /// <summary>
    /// Get one stored policy by ID.
    /// </summary>
    [HttpGet("{policyId:guid}")]
    [ProducesResponseType(typeof(ManagedPolicyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ManagedPolicyResponse>> Get(Guid policyId)
    {
        var policy = await _db.Policies.AsNoTracking()
            .FirstOrDefaultAsync(value => value.PolicyId == policyId);
        if (policy is null)
            return NotFound();

        var assignmentCounts = await GetCurrentPolicyAssignmentCountsAsync();
        assignmentCounts.TryGetValue(policyId, out var counts);
        var metadata = ReadPolicyMetadata(policy.PolicyJson, policy.PolicyId);

        return Ok(new ManagedPolicyResponse
        {
            PolicyId = policy.PolicyId,
            Name = metadata.Name,
            Description = metadata.Description,
            Type = metadata.Type,
            Status = policy.IsActive ? "Active" : "Archived",
            Version = policy.CurrentVersion,
            DeviceCount = counts.Total,
            CompliantDeviceCount = counts.Compliant,
            CompliancePercentage = counts.Total > 0
                ? (int)Math.Round(100d * counts.Compliant / counts.Total)
                : 0,
            Configuration = metadata.Configuration,
            CreatedAt = policy.CreatedAt,
            UpdatedAt = policy.ModifiedAt
        });
    }

    /// <summary>
    /// List enrolled devices and their latest policy delivery acknowledgement.
    /// Compliance here means the assigned policy version has been acknowledged.
    /// </summary>
    [HttpGet("compliance-report")]
    [ProducesResponseType(typeof(IReadOnlyList<PolicyManagementDeviceResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PolicyManagementDeviceResponse>>> GetComplianceReport()
    {
        return Ok(await GetManagementDevicesAsync(assignableOnly: false));
    }

    /// <summary>
    /// List active devices that can receive a policy assignment.
    /// </summary>
    [HttpGet("assignable-devices")]
    [ProducesResponseType(typeof(IReadOnlyList<PolicyManagementDeviceResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PolicyManagementDeviceResponse>>> GetAssignableDevices()
    {
        return Ok(await GetManagementDevicesAsync(assignableOnly: true));
    }

    private async Task<IReadOnlyList<PolicyManagementDeviceResponse>> GetManagementDevicesAsync(bool assignableOnly)
    {
        var devicesQuery = _db.Devices.AsNoTracking();
        if (assignableOnly)
            devicesQuery = devicesQuery.Where(device =>
                device.State == DeviceState.Active && !device.AccessDisabled);

        var devices = await devicesQuery
            .OrderBy(device => device.LastSeenAt)
            .Select(device => new
            {
                device.DeviceId,
                device.EnrollmentId,
                device.Manufacturer,
                device.Model,
                device.State,
                device.LastSeenAt
            })
            .ToListAsync();

        var latestAssignments = await _db.PolicyAssignments.AsNoTracking()
            .Include(assignment => assignment.Policy)
            .OrderByDescending(assignment => assignment.AssignedAt)
            .ToListAsync();
        var assignmentsByEnrollment = latestAssignments
            .GroupBy(assignment => assignment.EnrollmentId)
            .ToDictionary(group => group.Key, group => group.First());

        return devices.Select(device =>
        {
            assignmentsByEnrollment.TryGetValue(device.EnrollmentId, out var assignment);
            var deliveryStatus = assignment is null
                ? "unassigned"
                : assignment.AcknowledgedVersion >= assignment.AssignedVersion
                    ? "compliant"
                    : assignment.AcknowledgedVersion.HasValue
                        ? "non-compliant"
                        : "pending";
            var policyName = assignment?.Policy is null
                ? null
                : ReadPolicyMetadata(assignment.Policy.PolicyJson, assignment.PolicyId).Name;

            return new PolicyManagementDeviceResponse
            {
                DeviceId = device.DeviceId,
                EnrollmentId = device.EnrollmentId,
                DeviceName = string.Join(" ", new[] { device.Manufacturer, device.Model }
                    .Where(value => !string.IsNullOrWhiteSpace(value))) is { Length: > 0 } name
                    ? name
                    : device.DeviceId.ToString(),
                DeviceState = device.State.ToString(),
                ComplianceStatus = deliveryStatus,
                CompliancePercentage = deliveryStatus == "compliant" ? 100 : 0,
                LastChecked = assignment?.LastReportedAt ?? device.LastSeenAt,
                PolicyId = assignment?.PolicyId,
                PolicyName = policyName,
                AssignedVersion = assignment?.AssignedVersion,
                AcknowledgedVersion = assignment?.AcknowledgedVersion
            };
        }).ToArray();
    }

    private async Task<Dictionary<Guid, (int Total, int Compliant)>> GetCurrentPolicyAssignmentCountsAsync()
    {
        var assignments = await _db.PolicyAssignments.AsNoTracking()
            .OrderByDescending(assignment => assignment.AssignedAt)
            .ToListAsync();
        return assignments
            .GroupBy(assignment => assignment.EnrollmentId)
            .Select(group => group.First())
            .GroupBy(assignment => assignment.PolicyId)
            .ToDictionary(
                group => group.Key,
                group => (
                    Total: group.Count(),
                    Compliant: group.Count(assignment =>
                        assignment.AcknowledgedVersion.HasValue &&
                        assignment.AcknowledgedVersion.Value >= assignment.AssignedVersion)));
    }

    private static (
        string Name,
        string Description,
        string Type,
        JsonElement Configuration) ReadPolicyMetadata(string policyJson, Guid policyId)
    {
        using var document = JsonDocument.Parse(policyJson);
        var root = document.RootElement;
        return (
            ReadString(root, "name") ?? $"Policy {policyId.ToString("N")[..8]}",
            ReadString(root, "description") ?? string.Empty,
            ReadString(root, "type") ?? "Custom",
            root.Clone());
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
        }

        return null;
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

    /// <summary>
    /// Generate and register SET_POLICY commands for active assignments.
    /// Idempotent: calling multiple times produces no duplicate commands.
    /// </summary>
    /// <returns>Number of commands generated</returns>
    /// <response code="200">Commands generated and registered</response>
    /// <response code="500">Database error during command generation</response>
    [HttpPost("publish-commands")]
    [ProducesResponseType(typeof(PublishCommandsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PublishCommandsResponse>> PublishCommands()
    {
        try
        {
            int published = await _commandGenerator.GenerateActiveAssignmentsAsync();
            return Ok(new PublishCommandsResponse { CommandsPublished = published });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new { error = $"Command generation failed: {ex.Message}" });
        }
    }
}

/// <summary>
/// Response from publish command request.
/// </summary>
public sealed record PublishCommandsResponse
{
    /// <summary>
    /// Number of SET_POLICY commands published.
    /// </summary>
    public required int CommandsPublished { get; init; }
}

/// <summary>
/// Policy metadata exposed to the management console.
/// </summary>
public sealed record ManagedPolicyResponse
{
    public required Guid PolicyId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Type { get; init; }
    public required string Status { get; init; }
    public required long Version { get; init; }
    public required int DeviceCount { get; init; }
    public required int CompliantDeviceCount { get; init; }
    public required int CompliancePercentage { get; init; }
    public required JsonElement Configuration { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Device enrollment and policy delivery state used by management workflows.
/// </summary>
public sealed record PolicyManagementDeviceResponse
{
    public required Guid DeviceId { get; init; }
    public required Guid EnrollmentId { get; init; }
    public required string DeviceName { get; init; }
    public required string DeviceState { get; init; }
    public required string ComplianceStatus { get; init; }
    public required int CompliancePercentage { get; init; }
    public required DateTimeOffset LastChecked { get; init; }
    public Guid? PolicyId { get; init; }
    public string? PolicyName { get; init; }
    public long? AssignedVersion { get; init; }
    public long? AcknowledgedVersion { get; init; }
}

/// <summary>
/// Paginated policy list response.
/// </summary>
public sealed record ManagedPoliciesResponse
{
    public required IReadOnlyList<ManagedPolicyResponse> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int PageNumber { get; init; }
    public required int PageSize { get; init; }
    public required int TotalPages { get; init; }
}
