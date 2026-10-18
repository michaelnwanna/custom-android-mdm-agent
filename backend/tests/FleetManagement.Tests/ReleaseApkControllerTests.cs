using System;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using FleetManagement.Api.Releases;
using FleetManagement.Persistence;
using FleetManagement.Releases;

namespace FleetManagement.Tests;

/// <summary>
/// Tests for ReleaseApkController.
/// Verifies APK download functionality with Range request support.
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

    [Fact]
    public async Task DownloadApkAsync_ReturnsBadRequest_WhenReleaseIdInvalid()
    {
        using var db = CreateDbContext();
        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);

        var result = await controller.DownloadApkAsync("not-a-guid");

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
    }

    [Fact]
    public async Task DownloadApkAsync_ReturnsNotFound_WhenReleaseDoesNotExist()
    {
        using var db = CreateDbContext();
        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);

        var releaseId = Guid.NewGuid().ToString();
        var result = await controller.DownloadApkAsync(releaseId);

        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, notFoundResult.StatusCode);
    }

    [Fact]
    public async Task DownloadApkAsync_ReturnsOk_WithFullFileHeaders()
    {
        using var db = CreateDbContext();

        var release = CreateTestRelease(Guid.NewGuid());
        db.Set<Release>().Add(release);
        await db.SaveChangesAsync();

        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);

        // Set up mock HttpContext
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

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
        db.Set<Release>().Add(release);
        await db.SaveChangesAsync();

        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);

        // Set up mock HttpContext with Range header
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Range = "bytes=0-1023";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

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
        db.Set<Release>().Add(release);
        await db.SaveChangesAsync();

        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Range = "bytes=1000000-1000999";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

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
        db.Set<Release>().Add(release);
        await db.SaveChangesAsync();

        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Range = "bytes=5000000-";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

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
        db.Set<Release>().Add(release);
        await db.SaveChangesAsync();

        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Range = "bytes=-1000";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

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
        db.Set<Release>().Add(release);
        await db.SaveChangesAsync();

        var factory = new FakeDbContextFactory(db);
        var controller = new ReleaseApkController(factory);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Range = "invalid-range";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

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
