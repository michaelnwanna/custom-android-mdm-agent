namespace FleetManagement.Policies.Tests;

using System;
using System.Text.Json;
using System.Threading.Tasks;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// Unit tests for PolicyService (no database).
/// Tests JSON validation and error handling.
/// </summary>
public sealed class PolicyServiceTests
{
    private static PendingEnrollment CreateTestEnrollment()
    {
        var now = DateTimeOffset.UtcNow;
        var cred = new BootstrapTokenCredential(
            Guid.NewGuid(),
            "0000000000000000000000000000000000000000000000000000000000000000",
            now,
            now.AddMinutes(15));
        return PendingEnrollment.Create(cred);
    }

    [Fact]
    public async Task CreatePolicyIdempotent_Valid_Json_Creates_Policy()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new PolicyService(db);

        var json = """{ "type": "restriction" }""";
        var id1 = await service.CreatePolicyIdempotentAsync(json, "admin");

        Assert.NotEqual(Guid.Empty, id1);

        var policy = await service.GetPolicyAsync(id1);
        Assert.NotNull(policy);
        Assert.Equal("admin", policy.Creator);
        Assert.Equal(1, policy.CurrentVersion);
        Assert.Equal(json, policy.PolicyJson);
    }

    [Fact]
    public async Task CreatePolicyIdempotent_Same_Json_Returns_Same_Id()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new PolicyService(db);

        var json = """{ "type": "restriction" }""";
        var id1 = await service.CreatePolicyIdempotentAsync(json, "admin");
        var id2 = await service.CreatePolicyIdempotentAsync(json, "admin");

        Assert.Equal(id1, id2);

        var count = await db.Set<Policy>().CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CreatePolicyIdempotent_Different_Json_Produces_Different_Id()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new PolicyService(db);

        var json1 = """{ "type": "restriction" }""";
        var json2 = """{ "type": "allowlist" }""";

        var id1 = await service.CreatePolicyIdempotentAsync(json1, "admin");
        var id2 = await service.CreatePolicyIdempotentAsync(json2, "admin");

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public async Task CreatePolicyIdempotent_Empty_Json_Throws()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new PolicyService(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreatePolicyIdempotentAsync("", "admin"));
    }

    [Fact]
    public async Task CreatePolicyIdempotent_Invalid_Json_Throws()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new PolicyService(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreatePolicyIdempotentAsync("not json", "admin"));
    }

    [Fact]
    public async Task CreatePolicyIdempotent_Non_Object_Json_Throws()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new PolicyService(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreatePolicyIdempotentAsync("[]", "admin"));
    }

    [Fact]
    public async Task CreatePolicyIdempotent_Oversized_Json_Throws()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new PolicyService(db);

        var oversized = "{" + new string('x', 100000) + "}";

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreatePolicyIdempotentAsync(oversized, "admin"));
    }

    [Fact]
    public async Task AssignPolicy_Creates_Assignment()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using (var db = new FleetDbContext(options))
        {
            var enrollment = CreateTestEnrollment();
            db.Set<PendingEnrollment>().Add(enrollment);
            await db.SaveChangesAsync();

            var service = new PolicyService(db);

            var policyId = await service.CreatePolicyIdempotentAsync("{}", "admin");
            var assignmentId = await service.AssignPolicyAsync(enrollment.Id, policyId);

            Assert.NotEqual(Guid.Empty, assignmentId);
        }

        using (var db = new FleetDbContext(options))
        {
            var assignment = await db.Set<PolicyAssignment>().FirstOrDefaultAsync();
            Assert.NotNull(assignment);
            Assert.Equal(1, assignment.AssignedVersion);
            Assert.Null(assignment.AcknowledgedVersion);
        }
    }

    [Fact]
    public async Task AssignPolicy_Unknown_Enrollment_Throws()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new PolicyService(db);

        var policyId = await service.CreatePolicyIdempotentAsync("{}", "admin");

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AssignPolicyAsync(Guid.NewGuid(), policyId));
    }

    [Fact]
    public async Task AssignPolicy_Unknown_Policy_Throws()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var enrollment = CreateTestEnrollment();
        db.Set<PendingEnrollment>().Add(enrollment);
        await db.SaveChangesAsync();

        var service = new PolicyService(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AssignPolicyAsync(enrollment.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task AssignPolicy_Inactive_Policy_Throws()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using (var db = new FleetDbContext(options))
        {
            var enrollment = CreateTestEnrollment();
            db.Set<PendingEnrollment>().Add(enrollment);

            var policy = new Policy
            {
                PolicyId = Guid.NewGuid(),
                Creator = "admin",
                CurrentVersion = 1,
                PolicyJson = "{}",
                IsActive = false
            };
            db.Set<Policy>().Add(policy);
            await db.SaveChangesAsync();

            var service = new PolicyService(db);

            await Assert.ThrowsAsync<ArgumentException>(
                () => service.AssignPolicyAsync(enrollment.Id, policy.PolicyId));
        }
    }
}
