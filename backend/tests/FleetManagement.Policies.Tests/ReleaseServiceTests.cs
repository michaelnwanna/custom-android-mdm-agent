namespace FleetManagement.Releases.Tests;

using System;
using System.Text.Json;
using System.Threading.Tasks;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// Integration tests for ReleaseService (with in-memory database).
/// Tests idempotent release creation, deployment workflows, and device state tracking.
/// </summary>
public sealed class ReleaseServiceTests
{
    private static readonly string ValidMetadataJson = @"{
  ""packageName"": ""com.example.app"",
  ""versionCode"": 42,
  ""apkSha256"": ""0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"",
  ""apkSizeBytes"": 1048576,
  ""minSdkLevel"": 29,
  ""expiresAt"": ""2099-12-31T23:59:59Z""
}";

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
    public async Task CreateReleaseIdempotent_Valid_Metadata_Creates_Release()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new ReleaseService(db);

        var releaseId = await service.CreateReleaseIdempotentAsync(ValidMetadataJson, "sig-1", "admin");

        Assert.NotEqual(Guid.Empty, releaseId);

        var release = await service.GetReleaseAsync(releaseId);
        Assert.NotNull(release);
        Assert.Equal("admin", release.CreatedBy);
        Assert.Equal(ValidMetadataJson, release.MetadataJson);
        Assert.Equal("sig-1", release.MetadataSignature);
        Assert.True(release.IsActive);
    }

    [Fact]
    public async Task CreateReleaseIdempotent_Same_Metadata_Returns_Same_Id()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new ReleaseService(db);

        var id1 = await service.CreateReleaseIdempotentAsync(ValidMetadataJson, "sig-1", "admin");
        var id2 = await service.CreateReleaseIdempotentAsync(ValidMetadataJson, "sig-1", "admin");

        Assert.Equal(id1, id2);

        var count = await db.Set<Release>().CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CreateReleaseIdempotent_Different_Metadata_Different_Id()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new ReleaseService(db);

        var json1 = ValidMetadataJson;
        var json2 = @"{
  ""packageName"": ""com.example.app"",
  ""versionCode"": 43,
  ""apkSha256"": ""0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"",
  ""apkSizeBytes"": 1048576,
  ""minSdkLevel"": 29,
  ""expiresAt"": ""2099-12-31T23:59:59Z""
}";

        var id1 = await service.CreateReleaseIdempotentAsync(json1, "sig-1", "admin");
        var id2 = await service.CreateReleaseIdempotentAsync(json2, "sig-1", "admin");

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public async Task CreateReleaseIdempotent_Same_Id_Different_Signature_Throws()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new ReleaseService(db);

        await service.CreateReleaseIdempotentAsync(ValidMetadataJson, "sig-1", "admin");

        // Same metadata, different signature should throw
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateReleaseIdempotentAsync(ValidMetadataJson, "sig-different", "admin"));
    }

    [Fact]
    public async Task CreateReleaseIdempotent_Empty_Metadata_Throws()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new ReleaseService(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateReleaseIdempotentAsync("", "sig", "admin"));
    }

    [Fact]
    public async Task CreateReleaseIdempotent_Invalid_Creator_Throws()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new ReleaseService(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateReleaseIdempotentAsync(ValidMetadataJson, "sig", ""));
    }

    [Fact]
    public async Task CreatePilotDeployment_Valid_Release_Creates_Deployment()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new ReleaseService(db);

        var releaseId = await service.CreateReleaseIdempotentAsync(ValidMetadataJson, "sig", "admin");
        var deploymentId = await service.CreatePilotDeploymentAsync(releaseId, "admin");

        Assert.NotEqual(Guid.Empty, deploymentId);

        var deployment = await service.GetDeploymentAsync(deploymentId);
        Assert.NotNull(deployment);
        Assert.Equal(DeploymentStage.Pilot, deployment.Stage);
        Assert.True(deployment.IsActive);
        Assert.Equal(releaseId, deployment.ReleaseId);
    }

    [Fact]
    public async Task CreatePilotDeployment_Unknown_Release_Throws()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new ReleaseService(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreatePilotDeploymentAsync(Guid.NewGuid(), "admin"));
    }

    [Fact]
    public async Task CreateStagedDeployment_Valid_Release_Creates_Deployment()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new ReleaseService(db);

        var releaseId = await service.CreateReleaseIdempotentAsync(ValidMetadataJson, "sig", "admin");
        var deploymentId = await service.CreateStagedDeploymentAsync(releaseId, 25, "admin");

        Assert.NotEqual(Guid.Empty, deploymentId);

        var deployment = await service.GetDeploymentAsync(deploymentId);
        Assert.NotNull(deployment);
        Assert.Equal(DeploymentStage.Staged, deployment.Stage);
        Assert.Equal(25, deployment.RolloutPercentage);
        Assert.Equal(releaseId, deployment.ReleaseId);
    }

    [Fact]
    public async Task CreateStagedDeployment_Invalid_Rollout_Percentage_Throws()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new ReleaseService(db);

        var releaseId = await service.CreateReleaseIdempotentAsync(ValidMetadataJson, "sig", "admin");

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateStagedDeploymentAsync(releaseId, 0, "admin"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateStagedDeploymentAsync(releaseId, 101, "admin"));
    }

    [Fact]
    public async Task PromoteStagedDeployment_Staged_To_Promoted()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new ReleaseService(db);

        var releaseId = await service.CreateReleaseIdempotentAsync(ValidMetadataJson, "sig", "admin");
        var stagedId = await service.CreateStagedDeploymentAsync(releaseId, 50, "admin");

        var promotedId = await service.PromoteStagedDeploymentAsync(stagedId);

        var promoted = await service.GetDeploymentAsync(promotedId);
        Assert.NotNull(promoted);
        Assert.Equal(DeploymentStage.Promoted, promoted.Stage);
        Assert.Equal(100, promoted.RolloutPercentage);
    }

    [Fact]
    public async Task PromoteStagedDeployment_Unknown_Deployment_Throws()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new ReleaseService(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.PromoteStagedDeploymentAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AssignDeployment_Unknown_Enrollment_Throws()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new FleetDbContext(options);
        var service = new ReleaseService(db);

        var releaseId = await service.CreateReleaseIdempotentAsync(ValidMetadataJson, "sig", "admin");
        var deploymentId = await service.CreatePilotDeploymentAsync(releaseId, "admin");

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AssignDeploymentAsync(deploymentId, Guid.NewGuid()));
    }
}

