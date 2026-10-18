using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using FleetManagement.Api;
using FleetManagement.Api.Releases;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using FleetManagement.Releases;

namespace FleetManagement.Tests;

/// <summary>
/// Tests for ReleaseApkController.
/// Verifies APK download functionality with Range request support and device authentication.
/// </summary>
public class ReleaseApkControllerTests
{
    private FleetDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(databaseName: $"ReleaseApkControllerTests_{Guid.NewGuid()}")
            .Options;
        return new FleetDbContext(options);
    }

    private Release CreateTestRelease(Guid releaseId)
    {
        var metadata = new ReleaseMetadata
        {
            PackageName = "com.example.app",
            VersionCode = 1,
            ApkSha256 = "abc123def456abc123def456abc123def456abc123def456abc123def456abc1",
            ApkSizeBytes = 10000000,
            MinSdkLevel = 26,
            ExpiresAt = DateTimeOffset.UtcNow.AddYears(1)
        };
        var metadataJson = JsonSerializer.Serialize(metadata);
        return Release.CreateFromMetadata(metadata, metadataJson, "test-signature", DateTimeOffset.UtcNow, "test-user");
    }

    private void SetupAuthenticatedDevice(ReleaseApkController controller, Guid deviceId)
    {
        var claims = new List<Claim>
        {
            new Claim("device_id", deviceId.ToString("D"))
        };
        var identity = new ClaimsIdentity(claims, DeviceAuthentication.Scheme);
        var principal = new ClaimsPrincipal(identity);
        controller.ControllerContext.HttpContext.User = principal;
    }

    private PendingEnrollment CreateTestEnrollment(Guid deviceId)
    {
        var tokenDigest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var issuedAt = new DateTimeOffset(DateTime.UtcNow, TimeSpan.Zero);
        var expiresAt = issuedAt.AddMinutes(10);
        var cred = new BootstrapTokenCredential(
            Guid.NewGuid(),
            tokenDigest,
            issuedAt,
            expiresAt);
        var enrollment = PendingEnrollment.Create(cred);
        // Set DeviceId via reflection
        typeof(PendingEnrollment).GetProperty("DeviceId")?.SetValue(enrollment, deviceId);
        return enrollment;
    }

    [Fact]
    public async Task DownloadApkAsync_ReturnsUnauthorized_WhenDeviceNotAuthenticated()
    {
        using var db = CreateDbContext();
        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.DownloadApkAsync(Guid.NewGuid().ToString());

        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorizedResult.StatusCode);
    }

    [Fact]
    public async Task DownloadApkAsync_ReturnsBadRequest_WhenReleaseIdInvalid()
    {
        using var db = CreateDbContext();
        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        SetupAuthenticatedDevice(controller, Guid.NewGuid());

        var result = await controller.DownloadApkAsync("not-a-guid");

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
    }

    [Fact]
    public async Task DownloadApkAsync_ReturnsForbidden_WhenDeviceNotEnrolled()
    {
        using var db = CreateDbContext();
        
        var release = CreateTestRelease(Guid.NewGuid());
        db.Set<Release>().Add(release);
        await db.SaveChangesAsync();

        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        var unauthenticatedDeviceId = Guid.NewGuid();
        SetupAuthenticatedDevice(controller, unauthenticatedDeviceId);

        var result = await controller.DownloadApkAsync(release.ReleaseId.ToString());

        var forbiddenResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbiddenResult.StatusCode);
    }

    [Fact]
    public async Task DownloadApkAsync_ReturnsNotFound_WhenReleaseDoesNotExist()
    {
        using var db = CreateDbContext();
        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        SetupAuthenticatedDevice(controller, Guid.NewGuid());

        var releaseId = Guid.NewGuid().ToString();
        var result = await controller.DownloadApkAsync(releaseId);

        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, notFoundResult.StatusCode);
    }

    [Fact]
    public async Task DownloadApkAsync_ReturnsForbidden_WhenDeviceHasNoDeployment()
    {
        using var db = CreateDbContext();

        var release = CreateTestRelease(Guid.NewGuid());
        var deviceId = Guid.NewGuid();
        var enrollment = CreateTestEnrollment(deviceId);

        db.Set<Release>().Add(release);
        db.Set<PendingEnrollment>().Add(enrollment);
        await db.SaveChangesAsync();

        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        SetupAuthenticatedDevice(controller, deviceId);

        var result = await controller.DownloadApkAsync(release.ReleaseId.ToString());

        var forbiddenResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbiddenResult.StatusCode);
    }

    [Fact]
    public async Task DownloadApkAsync_ReturnsOk_WhenDeviceHasActiveDeployment()
    {
        using var db = CreateDbContext();

        var release = CreateTestRelease(Guid.NewGuid());
        var deviceId = Guid.NewGuid();
        var enrollment = CreateTestEnrollment(deviceId);
        var deployment = ReleaseDeployment.CreatePilot(release.ReleaseId, DateTimeOffset.UtcNow, "test-user");
        var deploymentDeviceState = DeploymentDeviceState.CreatePending(deployment.DeploymentId, enrollment.Id, DateTimeOffset.UtcNow);

        db.Set<Release>().Add(release);
        db.Set<PendingEnrollment>().Add(enrollment);
        db.Set<ReleaseDeployment>().Add(deployment);
        db.Set<DeploymentDeviceState>().Add(deploymentDeviceState);
        await db.SaveChangesAsync();

        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        SetupAuthenticatedDevice(controller, deviceId);

        var result = await controller.DownloadApkAsync(release.ReleaseId.ToString());

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Equal(release.ApkSizeBytes, httpContext.Response.ContentLength);
        Assert.Equal("application/vnd.android.package-archive", httpContext.Response.ContentType);
        Assert.Equal("bytes", httpContext.Response.Headers.AcceptRanges.ToString());
    }

    [Fact]
    public async Task DownloadApkAsync_ReturnsPartialContent_WithRangeHeader()
    {
        using var db = CreateDbContext();

        var release = CreateTestRelease(Guid.NewGuid());
        var deviceId = Guid.NewGuid();
        var enrollment = CreateTestEnrollment(deviceId);
        var deployment = ReleaseDeployment.CreatePilot(release.ReleaseId, DateTimeOffset.UtcNow, "test-user");
        var deploymentDeviceState = DeploymentDeviceState.CreatePending(deployment.DeploymentId, enrollment.Id, DateTimeOffset.UtcNow);

        db.Set<Release>().Add(release);
        db.Set<PendingEnrollment>().Add(enrollment);
        db.Set<ReleaseDeployment>().Add(deployment);
        db.Set<DeploymentDeviceState>().Add(deploymentDeviceState);
        await db.SaveChangesAsync();

        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Range = "bytes=0-1023";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        SetupAuthenticatedDevice(controller, deviceId);

        var result = await controller.DownloadApkAsync(release.ReleaseId.ToString());

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status206PartialContent, httpContext.Response.StatusCode);
        Assert.Equal("bytes 0-1023/" + release.ApkSizeBytes, httpContext.Response.Headers.ContentRange.ToString());
        Assert.Equal(1024L, httpContext.Response.ContentLength);
    }

    [Fact]
    public async Task DownloadApkAsync_ReturnsPartialContent_WithMiddleRangeRequest()
    {
        using var db = CreateDbContext();

        var release = CreateTestRelease(Guid.NewGuid());
        var deviceId = Guid.NewGuid();
        var enrollment = CreateTestEnrollment(deviceId);
        var deployment = ReleaseDeployment.CreatePilot(release.ReleaseId, DateTimeOffset.UtcNow, "test-user");
        var deploymentDeviceState = DeploymentDeviceState.CreatePending(deployment.DeploymentId, enrollment.Id, DateTimeOffset.UtcNow);

        db.Set<Release>().Add(release);
        db.Set<PendingEnrollment>().Add(enrollment);
        db.Set<ReleaseDeployment>().Add(deployment);
        db.Set<DeploymentDeviceState>().Add(deploymentDeviceState);
        await db.SaveChangesAsync();

        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Range = "bytes=1000000-1000999";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        SetupAuthenticatedDevice(controller, deviceId);

        var result = await controller.DownloadApkAsync(release.ReleaseId.ToString());

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status206PartialContent, httpContext.Response.StatusCode);
        Assert.Equal("bytes 1000000-1000999/" + release.ApkSizeBytes, httpContext.Response.Headers.ContentRange.ToString());
        Assert.Equal(1000L, httpContext.Response.ContentLength);
    }

    [Fact]
    public async Task DownloadApkAsync_ReturnsPartialContent_WithOpenEndedRange()
    {
        using var db = CreateDbContext();

        var release = CreateTestRelease(Guid.NewGuid());
        var deviceId = Guid.NewGuid();
        var enrollment = CreateTestEnrollment(deviceId);
        var deployment = ReleaseDeployment.CreatePilot(release.ReleaseId, DateTimeOffset.UtcNow, "test-user");
        var deploymentDeviceState = DeploymentDeviceState.CreatePending(deployment.DeploymentId, enrollment.Id, DateTimeOffset.UtcNow);

        db.Set<Release>().Add(release);
        db.Set<PendingEnrollment>().Add(enrollment);
        db.Set<ReleaseDeployment>().Add(deployment);
        db.Set<DeploymentDeviceState>().Add(deploymentDeviceState);
        await db.SaveChangesAsync();

        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Range = "bytes=5000000-";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        SetupAuthenticatedDevice(controller, deviceId);

        var result = await controller.DownloadApkAsync(release.ReleaseId.ToString());

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status206PartialContent, httpContext.Response.StatusCode);
        var contentRange = httpContext.Response.Headers.ContentRange.ToString();
        Assert.StartsWith("bytes 5000000-", contentRange);
        Assert.EndsWith($"/{release.ApkSizeBytes}", contentRange);
    }

    [Fact]
    public async Task DownloadApkAsync_ReturnsPartialContent_WithSuffixRange()
    {
        using var db = CreateDbContext();

        var release = CreateTestRelease(Guid.NewGuid());
        var deviceId = Guid.NewGuid();
        var enrollment = CreateTestEnrollment(deviceId);
        var deployment = ReleaseDeployment.CreatePilot(release.ReleaseId, DateTimeOffset.UtcNow, "test-user");
        var deploymentDeviceState = DeploymentDeviceState.CreatePending(deployment.DeploymentId, enrollment.Id, DateTimeOffset.UtcNow);

        db.Set<Release>().Add(release);
        db.Set<PendingEnrollment>().Add(enrollment);
        db.Set<ReleaseDeployment>().Add(deployment);
        db.Set<DeploymentDeviceState>().Add(deploymentDeviceState);
        await db.SaveChangesAsync();

        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Range = "bytes=-1000";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        SetupAuthenticatedDevice(controller, deviceId);

        var result = await controller.DownloadApkAsync(release.ReleaseId.ToString());

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status206PartialContent, httpContext.Response.StatusCode);
        var expectedStart = release.ApkSizeBytes - 1000;
        var contentRange = httpContext.Response.Headers.ContentRange.ToString();
        Assert.Equal($"bytes {expectedStart}-{release.ApkSizeBytes - 1}/{release.ApkSizeBytes}", contentRange);
    }

    [Fact]
    public async Task DownloadApkAsync_ReturnsOk_WithInvalidRangeHeader()
    {
        using var db = CreateDbContext();

        var release = CreateTestRelease(Guid.NewGuid());
        var deviceId = Guid.NewGuid();
        var enrollment = CreateTestEnrollment(deviceId);
        var deployment = ReleaseDeployment.CreatePilot(release.ReleaseId, DateTimeOffset.UtcNow, "test-user");
        var deploymentDeviceState = DeploymentDeviceState.CreatePending(deployment.DeploymentId, enrollment.Id, DateTimeOffset.UtcNow);

        db.Set<Release>().Add(release);
        db.Set<PendingEnrollment>().Add(enrollment);
        db.Set<ReleaseDeployment>().Add(deployment);
        db.Set<DeploymentDeviceState>().Add(deploymentDeviceState);
        await db.SaveChangesAsync();

        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Range = "invalid-range";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        SetupAuthenticatedDevice(controller, deviceId);

        var result = await controller.DownloadApkAsync(release.ReleaseId.ToString());

        // Invalid range should fall back to full file
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
    }
}

internal class FakeDbContextFactory : IDbContextFactory<FleetDbContext>
{
    private readonly FleetDbContext _db;

    public FakeDbContextFactory(FleetDbContext db)
    {
        _db = db;
    }

    public FleetDbContext CreateDbContext()
    {
        return _db;
    }

    public ValueTask<FleetDbContext> CreateDbContextAsync()
    {
        return new ValueTask<FleetDbContext>(_db);
    }
}
