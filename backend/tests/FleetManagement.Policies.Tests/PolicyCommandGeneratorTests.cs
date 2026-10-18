namespace FleetManagement.Policies.Tests;

using System;
using System.Threading.Tasks;
using FleetManagement.Enrollment;
using FleetManagement.Messaging;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// Integration tests for PolicyCommandGenerator.
/// Tests command generation and registration with in-memory database.
/// </summary>
public sealed class PolicyCommandGeneratorTests
{
    private static async Task<PendingEnrollment> CreateEnrolledDeviceAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var cred = new BootstrapTokenCredential(
            Guid.NewGuid(),
            "0000000000000000000000000000000000000000000000000000000000000000",
            now,
            now.AddMinutes(15));
        var enrollment = PendingEnrollment.Create(cred);

        // Manually set DeviceId via reflection (internal property)
        var deviceIdProp = typeof(PendingEnrollment).GetProperty("DeviceId");
        deviceIdProp?.SetValue(enrollment, Guid.NewGuid());

        return enrollment;
    }

    [Fact]
    public async Task GenerateActiveAssignments_Skips_Unacknowledged_Assignment()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using (var ctx = new FleetDbContext(options))
        {
            var enrollment = await CreateEnrolledDeviceAsync();
            ctx.Set<PendingEnrollment>().Add(enrollment);

            var policy = new Policy
            {
                PolicyId = Guid.NewGuid(),
                Creator = "admin",
                CurrentVersion = 1,
                PolicyJson = "{}",
                IsActive = true
            };
            ctx.Set<Policy>().Add(policy);

            var assignment = new PolicyAssignment
            {
                AssignmentId = Guid.NewGuid(),
                EnrollmentId = enrollment.Id,
                PolicyId = policy.PolicyId,
                AssignedVersion = 1,
                AssignedAt = DateTimeOffset.UtcNow
            };
            ctx.Set<PolicyAssignment>().Add(assignment);
            await ctx.SaveChangesAsync();
        }

        using (var db = new FleetDbContext(options))
        {
            var generator = new PolicyCommandGenerator(
                new InMemoryDbContextFactory(options),
                TimeProvider.System);

            var count = await generator.GenerateActiveAssignmentsAsync();

            Assert.Equal(1, count);

            var ownership = await db.Set<CommandEventOwnership>()
                .FirstOrDefaultAsync();
            Assert.NotNull(ownership);
        }
    }

    [Fact]
    public async Task GenerateActiveAssignments_Skips_Inactive_Policies()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using (var ctx = new FleetDbContext(options))
        {
            var enrollment = await CreateEnrolledDeviceAsync();
            ctx.Set<PendingEnrollment>().Add(enrollment);

            var policy = new Policy
            {
                PolicyId = Guid.NewGuid(),
                Creator = "admin",
                CurrentVersion = 1,
                PolicyJson = "{}",
                IsActive = false
            };
            ctx.Set<Policy>().Add(policy);

            var assignment = new PolicyAssignment
            {
                AssignmentId = Guid.NewGuid(),
                EnrollmentId = enrollment.Id,
                PolicyId = policy.PolicyId,
                AssignedVersion = 1,
                AssignedAt = DateTimeOffset.UtcNow
            };
            ctx.Set<PolicyAssignment>().Add(assignment);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = new FleetDbContext(options))
        {
            var generator = new PolicyCommandGenerator(
                new InMemoryDbContextFactory(options),
                TimeProvider.System);

            var count = await generator.GenerateActiveAssignmentsAsync();

            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task GenerateActiveAssignments_Skips_Unenrolled_Devices()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using (var ctx = new FleetDbContext(options))
        {
            var now = DateTimeOffset.UtcNow;
            var cred = new BootstrapTokenCredential(
                Guid.NewGuid(),
                "0000000000000000000000000000000000000000000000000000000000000000",
                now,
                now.AddMinutes(15));
            var enrollment = PendingEnrollment.Create(cred); // Not enrolled (no DeviceId)

            ctx.Set<PendingEnrollment>().Add(enrollment);

            var policy = new Policy
            {
                PolicyId = Guid.NewGuid(),
                Creator = "admin",
                CurrentVersion = 1,
                PolicyJson = "{}",
                IsActive = true
            };
            ctx.Set<Policy>().Add(policy);

            var assignment = new PolicyAssignment
            {
                AssignmentId = Guid.NewGuid(),
                EnrollmentId = enrollment.Id,
                PolicyId = policy.PolicyId,
                AssignedVersion = 1,
                AssignedAt = DateTimeOffset.UtcNow
            };
            ctx.Set<PolicyAssignment>().Add(assignment);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = new FleetDbContext(options))
        {
            var generator = new PolicyCommandGenerator(
                new InMemoryDbContextFactory(options),
                TimeProvider.System);

            var count = await generator.GenerateActiveAssignmentsAsync();

            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task GenerateActiveAssignments_Idempotent()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using (var ctx = new FleetDbContext(options))
        {
            var enrollment = await CreateEnrolledDeviceAsync();
            ctx.Set<PendingEnrollment>().Add(enrollment);

            var policy = new Policy
            {
                PolicyId = Guid.NewGuid(),
                Creator = "admin",
                CurrentVersion = 1,
                PolicyJson = "{}",
                IsActive = true
            };
            ctx.Set<Policy>().Add(policy);

            var assignment = new PolicyAssignment
            {
                AssignmentId = Guid.NewGuid(),
                EnrollmentId = enrollment.Id,
                PolicyId = policy.PolicyId,
                AssignedVersion = 1,
                AssignedAt = DateTimeOffset.UtcNow
            };
            ctx.Set<PolicyAssignment>().Add(assignment);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = new FleetDbContext(options))
        {
            var generator = new PolicyCommandGenerator(
                new InMemoryDbContextFactory(options),
                TimeProvider.System);

            // First generation
            var count1 = await generator.GenerateActiveAssignmentsAsync();
            Assert.Equal(1, count1);

            // Second generation (should be idempotent)
            var count2 = await generator.GenerateActiveAssignmentsAsync();
            Assert.Equal(0, count2);

            // Verify only one ownership entry
            var count = await ctx.Set<CommandEventOwnership>().CountAsync();
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public async Task GenerateActiveAssignments_Filters_By_EnrollmentId()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        Guid enrollment1Id;
        Guid enrollment2Id;

        using (var ctx = new FleetDbContext(options))
        {
            var enrollment1 = await CreateEnrolledDeviceAsync();
            var enrollment2 = await CreateEnrolledDeviceAsync();
            enrollment1Id = enrollment1.Id;
            enrollment2Id = enrollment2.Id;

            ctx.Set<PendingEnrollment>().Add(enrollment1);
            ctx.Set<PendingEnrollment>().Add(enrollment2);

            var policy = new Policy
            {
                PolicyId = Guid.NewGuid(),
                Creator = "admin",
                CurrentVersion = 1,
                PolicyJson = "{}",
                IsActive = true
            };
            ctx.Set<Policy>().Add(policy);

            var assignment1 = new PolicyAssignment
            {
                AssignmentId = Guid.NewGuid(),
                EnrollmentId = enrollment1Id,
                PolicyId = policy.PolicyId,
                AssignedVersion = 1,
                AssignedAt = DateTimeOffset.UtcNow
            };
            var assignment2 = new PolicyAssignment
            {
                AssignmentId = Guid.NewGuid(),
                EnrollmentId = enrollment2Id,
                PolicyId = policy.PolicyId,
                AssignedVersion = 1,
                AssignedAt = DateTimeOffset.UtcNow
            };

            ctx.Set<PolicyAssignment>().Add(assignment1);
            ctx.Set<PolicyAssignment>().Add(assignment2);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = new FleetDbContext(options))
        {
            var generator = new PolicyCommandGenerator(
                new InMemoryDbContextFactory(options),
                TimeProvider.System);

            // Generate only for enrollment1
            var count = await generator.GenerateActiveAssignmentsAsync(enrollment1Id);

            Assert.Equal(1, count);

            var ownerships = await ctx.Set<CommandEventOwnership>()
                .Where(x => x.EnrollmentId == enrollment1Id)
                .CountAsync();

            Assert.Equal(1, ownerships);
        }
    }
}

/// <summary>
/// Helper for providing in-memory DbContextFactory in tests.
/// </summary>
public sealed class InMemoryDbContextFactory : IDbContextFactory<FleetDbContext>
{
    private readonly DbContextOptions<FleetDbContext> _options;

    public InMemoryDbContextFactory(DbContextOptions<FleetDbContext> options)
    {
        _options = options;
    }

    public FleetDbContext CreateDbContext() => new(_options);

    public ValueTask<FleetDbContext> CreateDbContextAsync()
        => ValueTask.FromResult(CreateDbContext());
}
