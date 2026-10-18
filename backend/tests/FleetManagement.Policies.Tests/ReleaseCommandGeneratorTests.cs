using System;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Microsoft.EntityFrameworkCore;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using FleetManagement.Releases;
using FleetManagement.Messaging;

namespace FleetManagement.Policies.Tests;

public class ReleaseCommandGeneratorTests
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

        var deviceIdProp = typeof(PendingEnrollment).GetProperty("DeviceId");
        deviceIdProp?.SetValue(enrollment, Guid.NewGuid());

        return enrollment;
    }

    private Release CreateTestRelease()
    {
        var metadata = new ReleaseMetadata
        {
            PackageName = "com.example.app",
            VersionCode = 1,
            ApkSha256 = "abc123def456abc123def456abc123def456abc123def456abc123def456abc1",
            ApkSizeBytes = 1000000,
            MinSdkLevel = 26,
            ExpiresAt = DateTimeOffset.UtcNow.AddYears(1)
        };
        var metadataJson = JsonSerializer.Serialize(metadata);
        return Release.CreateFromMetadata(metadata, metadataJson, "test-signature", DateTimeOffset.UtcNow, "test-user");
    }

    [Fact]
    public async Task GenerateActiveAssignmentsAsync_ReturnsZeroWhenNoAssignments()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using (var db = new FleetDbContext(options))
        {
            var generator = new ReleaseCommandGenerator(
                new InMemoryDbContextFactory(options), TimeProvider.System);

            var count = await generator.GenerateActiveAssignmentsAsync();

            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task GenerateActiveAssignmentsAsync_SkipsCompletedDeviceStates()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using (var ctx = new FleetDbContext(options))
        {
            var enrollment = await CreateEnrolledDeviceAsync();
            ctx.Set<PendingEnrollment>().Add(enrollment);

            var release = CreateTestRelease();
            ctx.Set<Release>().Add(release);

            var deploymentId = Guid.NewGuid();
            var deployment = ReleaseDeployment.CreatePilot(release.ReleaseId, DateTimeOffset.UtcNow, "test");
            ctx.Set<ReleaseDeployment>().Add(deployment);

            var completedState = DeploymentDeviceState.CreatePending(deploymentId, enrollment.Id, DateTimeOffset.UtcNow);
            completedState.UpdateStatus(DeploymentDeviceStatus.Completed, null, DateTimeOffset.UtcNow);
            ctx.Set<DeploymentDeviceState>().Add(completedState);

            await ctx.SaveChangesAsync();
        }

        using (var db = new FleetDbContext(options))
        {
            var generator = new ReleaseCommandGenerator(
                new InMemoryDbContextFactory(options), TimeProvider.System);

            var count = await generator.GenerateActiveAssignmentsAsync();

            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task GenerateActiveAssignmentsAsync_GeneratesCommandForPendingState()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using (var ctx = new FleetDbContext(options))
        {
            var enrollment = await CreateEnrolledDeviceAsync();
            ctx.Set<PendingEnrollment>().Add(enrollment);

            var release = CreateTestRelease();
            ctx.Set<Release>().Add(release);

            var deploymentId = Guid.NewGuid();
            var deployment = ReleaseDeployment.CreatePilot(release.ReleaseId, DateTimeOffset.UtcNow, "test");
            ctx.Set<ReleaseDeployment>().Add(deployment);

            var state = DeploymentDeviceState.CreatePending(deploymentId, enrollment.Id, DateTimeOffset.UtcNow);
            ctx.Set<DeploymentDeviceState>().Add(state);

            await ctx.SaveChangesAsync();
        }

        using (var db = new FleetDbContext(options))
        {
            var generator = new ReleaseCommandGenerator(
                new InMemoryDbContextFactory(options), TimeProvider.System);

            var count = await generator.GenerateActiveAssignmentsAsync();

            Assert.Equal(1, count);

            var ownership = await db.Set<CommandEventOwnership>().FirstOrDefaultAsync();
            Assert.NotNull(ownership);
        }
    }

    [Fact]
    public async Task GenerateActiveAssignmentsAsync_IsIdempotent()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using (var ctx = new FleetDbContext(options))
        {
            var enrollment = await CreateEnrolledDeviceAsync();
            ctx.Set<PendingEnrollment>().Add(enrollment);

            var release = CreateTestRelease();
            ctx.Set<Release>().Add(release);

            var deploymentId = Guid.NewGuid();
            var deployment = ReleaseDeployment.CreatePilot(release.ReleaseId, DateTimeOffset.UtcNow, "test");
            ctx.Set<ReleaseDeployment>().Add(deployment);

            var state = DeploymentDeviceState.CreatePending(deploymentId, enrollment.Id, DateTimeOffset.UtcNow);
            ctx.Set<DeploymentDeviceState>().Add(state);

            await ctx.SaveChangesAsync();
        }

        using (var db = new FleetDbContext(options))
        {
            var generator = new ReleaseCommandGenerator(
                new InMemoryDbContextFactory(options), TimeProvider.System);

            var count1 = await generator.GenerateActiveAssignmentsAsync();
            Assert.Equal(1, count1);

            var ownershipCount1 = await db.Set<CommandEventOwnership>().CountAsync();
            Assert.Equal(1, ownershipCount1);

            var count2 = await generator.GenerateActiveAssignmentsAsync();
            Assert.Equal(0, count2);

            var ownershipCount2 = await db.Set<CommandEventOwnership>().CountAsync();
            Assert.Equal(1, ownershipCount2);
        }
    }

    [Fact]
    public async Task GenerateActiveAssignmentsAsync_FiltersByEnrollmentWhenSpecified()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using (var ctx = new FleetDbContext(options))
        {
            var release = CreateTestRelease();
            ctx.Set<Release>().Add(release);

            var deploymentId = Guid.NewGuid();
            var deployment = ReleaseDeployment.CreatePilot(release.ReleaseId, DateTimeOffset.UtcNow, "test");
            ctx.Set<ReleaseDeployment>().Add(deployment);

            var enrollment1 = await CreateEnrolledDeviceAsync();
            ctx.Set<PendingEnrollment>().Add(enrollment1);

            var enrollment2 = await CreateEnrolledDeviceAsync();
            ctx.Set<PendingEnrollment>().Add(enrollment2);

            var state1 = DeploymentDeviceState.CreatePending(deploymentId, enrollment1.Id, DateTimeOffset.UtcNow);
            ctx.Set<DeploymentDeviceState>().Add(state1);

            var state2 = DeploymentDeviceState.CreatePending(deploymentId, enrollment2.Id, DateTimeOffset.UtcNow);
            ctx.Set<DeploymentDeviceState>().Add(state2);

            await ctx.SaveChangesAsync();
        }

        using (var db = new FleetDbContext(options))
        {
            var generator = new ReleaseCommandGenerator(
                new InMemoryDbContextFactory(options), TimeProvider.System);

            var enrollment1Id = (await db.Set<PendingEnrollment>().FirstAsync()).Id;
            var count = await generator.GenerateActiveAssignmentsAsync(enrollment1Id);

            Assert.Equal(1, count);

            var ownerships = await db.Set<CommandEventOwnership>().ToListAsync();
            Assert.Single(ownerships);
        }
    }

    [Fact]
    public async Task GenerateForDeploymentAndDeviceAsync_SkipsCompletedState()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using (var ctx = new FleetDbContext(options))
        {
            var enrollment = await CreateEnrolledDeviceAsync();
            ctx.Set<PendingEnrollment>().Add(enrollment);

            var release = CreateTestRelease();
            ctx.Set<Release>().Add(release);

            var deploymentId = Guid.NewGuid();
            var deployment = ReleaseDeployment.CreatePilot(release.ReleaseId, DateTimeOffset.UtcNow, "test");
            ctx.Set<ReleaseDeployment>().Add(deployment);

            var state = DeploymentDeviceState.CreatePending(deploymentId, enrollment.Id, DateTimeOffset.UtcNow);
            state.UpdateStatus(DeploymentDeviceStatus.Completed, null, DateTimeOffset.UtcNow);
            ctx.Set<DeploymentDeviceState>().Add(state);

            await ctx.SaveChangesAsync();
        }

        using (var db = new FleetDbContext(options))
        {
            var enrollment = await db.Set<PendingEnrollment>().FirstAsync();
            var deployment = await db.Set<ReleaseDeployment>().FirstAsync();

            var generator = new ReleaseCommandGenerator(
                new InMemoryDbContextFactory(options), TimeProvider.System);

            var count = await generator.GenerateForDeploymentAndDeviceAsync(deployment.DeploymentId, enrollment.Id);

            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task GenerateForDeploymentAndDeviceAsync_GeneratesCommandSuccessfully()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using (var ctx = new FleetDbContext(options))
        {
            var enrollment = await CreateEnrolledDeviceAsync();
            ctx.Set<PendingEnrollment>().Add(enrollment);

            var release = CreateTestRelease();
            ctx.Set<Release>().Add(release);

            var deploymentId = Guid.NewGuid();
            var deployment = ReleaseDeployment.CreatePilot(release.ReleaseId, DateTimeOffset.UtcNow, "test");
            ctx.Set<ReleaseDeployment>().Add(deployment);

            var state = DeploymentDeviceState.CreatePending(deploymentId, enrollment.Id, DateTimeOffset.UtcNow);
            ctx.Set<DeploymentDeviceState>().Add(state);

            await ctx.SaveChangesAsync();
        }

        using (var db = new FleetDbContext(options))
        {
            var enrollment = await db.Set<PendingEnrollment>().FirstAsync();
            var deployment = await db.Set<ReleaseDeployment>().FirstAsync();

            var generator = new ReleaseCommandGenerator(
                new InMemoryDbContextFactory(options), TimeProvider.System);

            var count = await generator.GenerateForDeploymentAndDeviceAsync(deployment.DeploymentId, enrollment.Id);

            Assert.Equal(1, count);

            var ownership = await db.Set<CommandEventOwnership>().FirstOrDefaultAsync();
            Assert.NotNull(ownership);
        }
    }
}
