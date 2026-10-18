namespace FleetManagement.Policies;

using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Service for policy creation, assignment, and delivery.
/// Enforces idempotent creation: same JSON always produces the same policy ID.
/// </summary>
public sealed class PolicyService
{
    private readonly FleetDbContext _db;

    public PolicyService(FleetDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Create or retrieve a policy. Idempotent: same JSON → same ID.
    /// </summary>
    /// <param name="policyJson">Compact JSON policy document (max 64 KB)</param>
    /// <param name="creator">Subject creating the policy</param>
    /// <returns>The policy ID (UUID derived from content hash)</returns>
    /// <exception cref="ArgumentException">If JSON is invalid or exceeds 64 KB</exception>
    public async Task<Guid> CreatePolicyIdempotentAsync(string policyJson, string creator)
    {
        if (string.IsNullOrWhiteSpace(policyJson))
            throw new ArgumentException("PolicyJson is required", nameof(policyJson));

        if (policyJson.Length > 65536)
            throw new ArgumentException("PolicyJson must not exceed 64 KB", nameof(policyJson));

        // Validate JSON structure
        try
        {
            using var doc = JsonDocument.Parse(policyJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("Policy must be a JSON object");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid JSON: {ex.Message}", nameof(policyJson), ex);
        }

        // Derive policy ID from JSON content (deterministic)
        var policyId = DeriveIdFromContent(policyJson);

        // Check if policy already exists
        var existing = await _db.Set<Policy>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.PolicyId == policyId);

        if (existing != null)
            return policyId;

        // Create new policy
        var policy = new Policy
        {
            PolicyId = policyId,
            Creator = creator,
            CurrentVersion = 1,
            PolicyJson = policyJson,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow
        };

        _db.Set<Policy>().Add(policy);
        await _db.SaveChangesAsync();

        return policyId;
    }

    /// <summary>
    /// Assign a policy to a device.
    /// </summary>
    /// <param name="enrollmentId">Enrolled device ID</param>
    /// <param name="policyId">Policy to assign</param>
    /// <returns>Assignment ID for tracking</returns>
    /// <exception cref="ArgumentException">If device or policy not found</exception>
    public async Task<Guid> AssignPolicyAsync(Guid enrollmentId, Guid policyId)
    {
        // Verify device is enrolled
        var device = await _db.Set<PendingEnrollment>().AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == enrollmentId);
        if (device == null)
            throw new ArgumentException($"Device {enrollmentId} is not enrolled", nameof(enrollmentId));

        // Verify policy exists and is active
        var policy = await _db.Set<Policy>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.PolicyId == policyId);
        if (policy == null)
            throw new ArgumentException($"Policy {policyId} not found", nameof(policyId));

        if (!policy.IsActive)
            throw new ArgumentException($"Policy {policyId} is not active", nameof(policyId));

        // Create assignment
        var assignment = new PolicyAssignment
        {
            AssignmentId = Guid.NewGuid(),
            EnrollmentId = enrollmentId,
            PolicyId = policyId,
            AssignedVersion = policy.CurrentVersion,
            AssignedAt = DateTimeOffset.UtcNow
        };

        _db.Set<PolicyAssignment>().Add(assignment);
        await _db.SaveChangesAsync();

        return assignment.AssignmentId;
    }

    /// <summary>
    /// Get a policy by ID.
    /// </summary>
    public async Task<Policy?> GetPolicyAsync(Guid policyId)
    {
        return await _db.Set<Policy>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.PolicyId == policyId);
    }

    /// <summary>
    /// Get the current assignment for a device (if any).
    /// </summary>
    public async Task<PolicyAssignment?> GetCurrentAssignmentAsync(Guid enrollmentId)
    {
        return await _db.Set<PolicyAssignment>().AsNoTracking()
            .OrderByDescending(a => a.AssignedAt)
            .FirstOrDefaultAsync(a => a.EnrollmentId == enrollmentId);
    }

    /// <summary>
    /// Get an assignment by ID.
    /// </summary>
    public async Task<PolicyAssignment?> GetAssignmentAsync(Guid assignmentId)
    {
        return await _db.Set<PolicyAssignment>().AsNoTracking()
            .FirstOrDefaultAsync(a => a.AssignmentId == assignmentId);
    }

    /// <summary>
    /// Derive a policy ID from JSON content using MD5 hash.
    /// Deterministic: same JSON always produces the same UUID.
    /// </summary>
    private static Guid DeriveIdFromContent(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(bytes);
        return new Guid(hash);
    }
}
