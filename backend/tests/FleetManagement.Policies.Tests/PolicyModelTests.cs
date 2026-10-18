namespace FleetManagement.Policies.Tests;

using System;
using System.Text.Json;
using Xunit;

/// <summary>
/// Unit tests for policy models and idempotent creation.
/// </summary>
public sealed class PolicyModelTests
{
    [Fact]
    public void Policy_Can_Create_With_Valid_Data()
    {
        var now = DateTimeOffset.UtcNow;
        var policy = new Policy
        {
            PolicyId = Guid.NewGuid(),
            Creator = "admin@example.com",
            CurrentVersion = 1,
            PolicyJson = """{ "type": "restriction", "apps": [] }""",
            IsActive = true,
            CreatedAt = now,
            ModifiedAt = now
        };

        Assert.NotEqual(Guid.Empty, policy.PolicyId);
        Assert.Equal("admin@example.com", policy.Creator);
        Assert.Equal(1, policy.CurrentVersion);
        Assert.True(policy.IsActive);
    }

    [Fact]
    public void Policy_Version_Can_Increment()
    {
        var policy = new Policy
        {
            PolicyId = Guid.NewGuid(),
            Creator = "admin",
            CurrentVersion = 1,
            PolicyJson = "{}",
            IsActive = true
        };

        policy.CurrentVersion = 2;

        Assert.Equal(2, policy.CurrentVersion);
    }

    [Fact]
    public void PolicyAssignment_Can_Create_With_Valid_Data()
    {
        var enrollment = Guid.NewGuid();
        var policy = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var assignment = new PolicyAssignment
        {
            AssignmentId = Guid.NewGuid(),
            EnrollmentId = enrollment,
            PolicyId = policy,
            AssignedVersion = 1,
            AssignedAt = now
        };

        Assert.NotEqual(Guid.Empty, assignment.AssignmentId);
        Assert.Equal(enrollment, assignment.EnrollmentId);
        Assert.Equal(policy, assignment.PolicyId);
        Assert.Equal(1, assignment.AssignedVersion);
        Assert.Null(assignment.AcknowledgedVersion);
        Assert.Null(assignment.LastReportedAt);
    }

    [Fact]
    public void PolicyAssignment_Can_Acknowledge_Version()
    {
        var assignment = new PolicyAssignment
        {
            AssignmentId = Guid.NewGuid(),
            EnrollmentId = Guid.NewGuid(),
            PolicyId = Guid.NewGuid(),
            AssignedVersion = 1,
            AssignedAt = DateTimeOffset.UtcNow
        };

        assignment.AcknowledgedVersion = 1;
        assignment.LastReportedAt = DateTimeOffset.UtcNow.AddSeconds(10);

        Assert.Equal(1, assignment.AcknowledgedVersion);
        Assert.NotNull(assignment.LastReportedAt);
    }

    [Fact]
    public void DevicePolicyState_Can_Create()
    {
        var state = new DevicePolicyState
        {
            CurrentPolicyId = Guid.NewGuid(),
            AssignedVersion = 1,
            AcknowledgedVersion = 0,
            PolicyJson = "{}"
        };

        Assert.NotEqual(Guid.Empty, state.CurrentPolicyId);
        Assert.Equal(1, state.AssignedVersion);
        Assert.Equal(0, state.AcknowledgedVersion);
    }

    [Fact]
    public void DevicePolicyState_Can_Transition_Versions()
    {
        var state = new DevicePolicyState
        {
            CurrentPolicyId = Guid.NewGuid(),
            AssignedVersion = 1,
            AcknowledgedVersion = 0,
            PolicyJson = "{}"
        };

        state.AcknowledgedVersion = 1;

        Assert.Equal(1, state.AcknowledgedVersion);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "type": "restriction" }""")]
    [InlineData("""{ "apps": [], "restrictions": {} }""")]
    public void Policy_Accepts_Various_Valid_Json(string json)
    {
        var policy = new Policy
        {
            PolicyId = Guid.NewGuid(),
            Creator = "admin",
            CurrentVersion = 1,
            PolicyJson = json,
            IsActive = true
        };

        Assert.Equal(json, policy.PolicyJson);
    }

    [Fact]
    public void Policy_JSON_Can_Be_Parsed()
    {
        var json = """{ "name": "TestPolicy", "version": 1 }""";
        var policy = new Policy
        {
            PolicyId = Guid.NewGuid(),
            Creator = "admin",
            CurrentVersion = 1,
            PolicyJson = json,
            IsActive = true
        };

        var doc = JsonDocument.Parse(policy.PolicyJson);
        Assert.Equal("TestPolicy", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public void PolicyAssignment_Multiple_Versions_Can_Track_State()
    {
        var enrollment = Guid.NewGuid();
        var policy = Guid.NewGuid();

        var assignment1 = new PolicyAssignment
        {
            AssignmentId = Guid.NewGuid(),
            EnrollmentId = enrollment,
            PolicyId = policy,
            AssignedVersion = 1,
            AssignedAt = DateTimeOffset.UtcNow
        };

        var assignment2 = new PolicyAssignment
        {
            AssignmentId = Guid.NewGuid(),
            EnrollmentId = enrollment,
            PolicyId = policy,
            AssignedVersion = 2,
            AssignedAt = DateTimeOffset.UtcNow.AddSeconds(30)
        };

        // Both assignments can exist for the same enrollment
        Assert.NotEqual(assignment1.AssignmentId, assignment2.AssignmentId);
        Assert.Equal(enrollment, assignment1.EnrollmentId);
        Assert.Equal(enrollment, assignment2.EnrollmentId);
        Assert.Equal(1, assignment1.AssignedVersion);
        Assert.Equal(2, assignment2.AssignedVersion);
    }

    [Fact]
    public void Policy_CreatedAt_Defaults_To_UtcNow()
    {
        var before = DateTimeOffset.UtcNow;
        var policy = new Policy
        {
            PolicyId = Guid.NewGuid(),
            Creator = "admin",
            CurrentVersion = 1,
            PolicyJson = "{}",
            IsActive = true
        };
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(policy.CreatedAt, before, after.AddSeconds(1));
    }

    [Fact]
    public void PolicyAssignment_Can_Track_Reporting()
    {
        var assignment = new PolicyAssignment
        {
            AssignmentId = Guid.NewGuid(),
            EnrollmentId = Guid.NewGuid(),
            PolicyId = Guid.NewGuid(),
            AssignedVersion = 1,
            AssignedAt = DateTimeOffset.UtcNow
        };

        var reportTime = DateTimeOffset.UtcNow.AddMinutes(5);
        assignment.LastReportedAt = reportTime;

        Assert.Equal(reportTime, assignment.LastReportedAt);
    }

    [Fact]
    public void Policy_Inactive_Flag_Works()
    {
        var policy = new Policy
        {
            PolicyId = Guid.NewGuid(),
            Creator = "admin",
            CurrentVersion = 1,
            PolicyJson = "{}",
            IsActive = true
        };

        Assert.True(policy.IsActive);

        policy.IsActive = false;

        Assert.False(policy.IsActive);
    }

    [Fact]
    public void DevicePolicyState_Stores_Policy_JSON()
    {
        var json = """{ "restrictions": { "apps": ["com.example.banned"] } }""";
        var state = new DevicePolicyState
        {
            CurrentPolicyId = Guid.NewGuid(),
            AssignedVersion = 1,
            AcknowledgedVersion = 0,
            PolicyJson = json
        };

        Assert.Equal(json, state.PolicyJson);

        var doc = JsonDocument.Parse(state.PolicyJson);
        var banned = doc.RootElement.GetProperty("restrictions").GetProperty("apps")[0].GetString();
        Assert.Equal("com.example.banned", banned);
    }
}
