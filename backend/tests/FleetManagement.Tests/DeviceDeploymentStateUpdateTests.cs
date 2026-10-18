using System;
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
/// Tests for device deployment state update functionality.
/// Verifies devices can report installation progress.
/// </summary>
public class DeviceDeploymentStateUpdateTests
{
    private FleetDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(databaseName: $"DeviceStateTests_{Guid.NewGuid()}")
            .Options;
        return new FleetDbContext(options);
    }

    [Fact]
    public async Task UpdateDeviceStatus_UpdatesDownloadingState()
    {
        using var db = CreateDbContext();

        var enrollmentId = Guid.NewGuid();
        var deploymentId = Guid.NewGuid();
        var deploymentDeviceState = DeploymentDeviceState.CreatePending(deploymentId, enrollmentId, DateTimeOffset.UtcNow);

        db.Set<DeploymentDeviceState>().Add(deploymentDeviceState);
        await db.SaveChangesAsync();

        var service = new ReleaseService(db);
        var httpContext = new DefaultHttpContext();
        var controller = new ReleaseController(service);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new UpdateDeviceStatusRequest { Status = "Downloading" };
        var result = await controller.UpdateDeviceStatusAsync(
            deploymentId.ToString(),
            enrollmentId.ToString(),
            request);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value as UpdateDeviceStatusResponse;
        Assert.NotNull(response);
        Assert.Equal("Downloading", response.Status);
        Assert.Equal(deploymentId.ToString(), response.DeploymentId);
    }

    [Fact]
    public async Task UpdateDeviceStatus_UpdatesInstallingState()
    {
        using var db = CreateDbContext();

        var enrollmentId = Guid.NewGuid();
        var deploymentId = Guid.NewGuid();
        var deploymentDeviceState = DeploymentDeviceState.CreatePending(deploymentId, enrollmentId, DateTimeOffset.UtcNow);

        db.Set<DeploymentDeviceState>().Add(deploymentDeviceState);
        await db.SaveChangesAsync();

        var service = new ReleaseService(db);
        var httpContext = new DefaultHttpContext();
        var controller = new ReleaseController(service);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new UpdateDeviceStatusRequest { Status = "Installing" };
        var result = await controller.UpdateDeviceStatusAsync(
            deploymentId.ToString(),
            enrollmentId.ToString(),
            request);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value as UpdateDeviceStatusResponse;
        Assert.NotNull(response);
        Assert.Equal("Installing", response.Status);
    }

    [Fact]
    public async Task UpdateDeviceStatus_UpdatesCompletedState()
    {
        using var db = CreateDbContext();

        var enrollmentId = Guid.NewGuid();
        var deploymentId = Guid.NewGuid();
        var deploymentDeviceState = DeploymentDeviceState.CreatePending(deploymentId, enrollmentId, DateTimeOffset.UtcNow);

        db.Set<DeploymentDeviceState>().Add(deploymentDeviceState);
        await db.SaveChangesAsync();

        var service = new ReleaseService(db);
        var httpContext = new DefaultHttpContext();
        var controller = new ReleaseController(service);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new UpdateDeviceStatusRequest { Status = "Completed" };
        var result = await controller.UpdateDeviceStatusAsync(
            deploymentId.ToString(),
            enrollmentId.ToString(),
            request);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value as UpdateDeviceStatusResponse;
        Assert.NotNull(response);
        Assert.Equal("Completed", response.Status);
    }

    [Fact]
    public async Task UpdateDeviceStatus_UpdatesFailedStateWithErrorMessage()
    {
        using var db = CreateDbContext();

        var enrollmentId = Guid.NewGuid();
        var deploymentId = Guid.NewGuid();
        var deploymentDeviceState = DeploymentDeviceState.CreatePending(deploymentId, enrollmentId, DateTimeOffset.UtcNow);

        db.Set<DeploymentDeviceState>().Add(deploymentDeviceState);
        await db.SaveChangesAsync();

        var service = new ReleaseService(db);
        var httpContext = new DefaultHttpContext();
        var controller = new ReleaseController(service);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var errorMsg = "APK signature verification failed";
        var request = new UpdateDeviceStatusRequest
        {
            Status = "Failed",
            ErrorMessage = errorMsg
        };
        var result = await controller.UpdateDeviceStatusAsync(
            deploymentId.ToString(),
            enrollmentId.ToString(),
            request);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value as UpdateDeviceStatusResponse;
        Assert.NotNull(response);
        Assert.Equal("Failed", response.Status);

        // Verify error message was stored
        var updatedState = await service.GetDeviceDeploymentStateAsync(deploymentId, enrollmentId);
        Assert.Equal(errorMsg, updatedState!.ErrorMessage);
    }

    [Fact]
    public async Task UpdateDeviceStatus_ReturnsNotFound_WhenStateDoesNotExist()
    {
        using var db = CreateDbContext();
        var service = new ReleaseService(db);
        var controller = new ReleaseController(service);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var deploymentId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        var request = new UpdateDeviceStatusRequest { Status = "Downloading" };

        var result = await controller.UpdateDeviceStatusAsync(
            deploymentId.ToString(),
            enrollmentId.ToString(),
            request);

        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, notFoundResult.StatusCode);
    }

    [Fact]
    public async Task UpdateDeviceStatus_ReturnsBadRequest_WithInvalidStatus()
    {
        using var db = CreateDbContext();
        var service = new ReleaseService(db);
        var controller = new ReleaseController(service);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var deploymentId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        var request = new UpdateDeviceStatusRequest { Status = "InvalidStatus" };

        var result = await controller.UpdateDeviceStatusAsync(
            deploymentId.ToString(),
            enrollmentId.ToString(),
            request);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
    }

    [Fact]
    public async Task UpdateDeviceStatus_ReturnsBadRequest_WhenDeploymentIdInvalid()
    {
        using var db = CreateDbContext();
        var service = new ReleaseService(db);
        var controller = new ReleaseController(service);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new UpdateDeviceStatusRequest { Status = "Downloading" };
        var result = await controller.UpdateDeviceStatusAsync(
            "not-a-guid",
            Guid.NewGuid().ToString(),
            request);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
    }

    [Fact]
    public async Task UpdateDeviceStatus_ReturnsBadRequest_WhenEnrollmentIdInvalid()
    {
        using var db = CreateDbContext();
        var service = new ReleaseService(db);
        var controller = new ReleaseController(service);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new UpdateDeviceStatusRequest { Status = "Downloading" };
        var result = await controller.UpdateDeviceStatusAsync(
            Guid.NewGuid().ToString(),
            "not-a-guid",
            request);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestResult.StatusCode);
    }
}
