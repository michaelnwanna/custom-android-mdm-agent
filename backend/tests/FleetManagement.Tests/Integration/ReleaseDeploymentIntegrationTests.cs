namespace FleetManagement.Tests.Integration;

using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FleetManagement.Api.Mqtt;
using FleetManagement.Api.Releases;
using FleetManagement.Devices;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using FleetManagement.Releases;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// End-to-end integration tests for the complete release deployment flow.
/// Tests: create release → create deployment → assign to device → process ack → verify state
/// </summary>
public class ReleaseDeploymentIntegrationTests
{
    private static readonly string ValidMetadataJson = @"{
  ""packageName"": ""com.example.app"",
  ""versionCode"": 42,
  ""apkSha256"": ""0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"",
  ""apkSizeBytes"": 1048576,
  ""minSdkLevel"": 29,
  ""expiresAt"": ""2099-12-31T23:59:59Z""
}";

    [Fact]
    public async Task FullFlow_CreateRelease_CreateDeployment_AssignToDevice()
    {
        // Arrange
        var dbContextFactory = new InMemoryDbContextFactory();
        var releaseService = new ReleaseService(dbContextFactory.CreateDbContext());
        var tagService = new DeviceTagService(dbContextFactory.CreateDbContext());
                var controller = new ReleaseController(releaseService, new BulkAssignmentService(dbContextFactory.CreateDbContext(), tagService, releaseService), null, dbContextFactory);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        // Act 1: Create Release
        var createReleaseResult = await controller.CreateReleaseAsync(
            new CreateReleaseRequest
            {
                MetadataJson = ValidMetadataJson,
                MetadataSignature = "test-signature"
            }
        );

        // Assert 1: Release created
        Assert.IsType<OkObjectResult>(createReleaseResult.Result);
        var releaseResponse = ((OkObjectResult)createReleaseResult.Result).Value as CreateReleaseResponse;
        Assert.NotNull(releaseResponse);
        var releaseId = Guid.Parse(releaseResponse.ReleaseId);

        // Act 2: Create Pilot Deployment
        using var db = dbContextFactory.CreateDbContext();
        var deploymentId = await releaseService.CreatePilotDeploymentAsync(releaseId, "test-admin");

        // Assert 2: Deployment created
        Assert.NotEqual(Guid.Empty, deploymentId);
        var deployment = await releaseService.GetDeploymentAsync(deploymentId);
        Assert.NotNull(deployment);
        Assert.Equal(DeploymentStage.Pilot, deployment.Stage);

        // Act 3: Create enrollment with device
        var enrollment = CreateEnrollment(db);
        db.Add(enrollment);
        await db.SaveChangesAsync();

        // Act 4: Assign deployment to device
        var assignResult = await controller.AssignDeploymentAsync(
            deploymentId.ToString(),
            new AssignDeploymentRequest { EnrollmentId = enrollment.Id.ToString() }
        );

        // Assert 4: Assignment successful
        Assert.IsType<OkObjectResult>(assignResult.Result);
        var assignResponse = ((OkObjectResult)assignResult.Result).Value as AssignDeploymentResponse;
        Assert.NotNull(assignResponse);
        Assert.Equal(deploymentId.ToString(), assignResponse.DeploymentId);

        // Act 5: Verify device state was created
        using var dbVerify = dbContextFactory.CreateDbContext();
        var deviceState = await dbVerify.DeploymentDeviceStates
            .FirstOrDefaultAsync(s =>
                s.DeploymentId == deploymentId &&
                s.EnrollmentId == enrollment.Id
            );

        // Assert 5: Device state tracking started
        Assert.NotNull(deviceState);
        Assert.Equal(DeploymentDeviceStatus.Pending, deviceState.Status);
    }

    [Fact]
    public async Task FullFlow_CreateRelease_Assignment_StateUpdate()
    {
        // Arrange
        var dbContextFactory = new InMemoryDbContextFactory();
        var releaseService = new ReleaseService(dbContextFactory.CreateDbContext());
        var tagService = new DeviceTagService(dbContextFactory.CreateDbContext());
                var controller = new ReleaseController(releaseService, new BulkAssignmentService(dbContextFactory.CreateDbContext(), tagService, releaseService), null, dbContextFactory);

        using var db = dbContextFactory.CreateDbContext();

        // Create release
        var releaseId = await releaseService.CreateReleaseIdempotentAsync(
            ValidMetadataJson,
            "test-signature",
            "test-admin"
        );

        // Create deployment
        var deploymentId = await releaseService.CreatePilotDeploymentAsync(releaseId, "test-admin");

        // Create enrollment
        var enrollment = CreateEnrollment(db);
        db.Add(enrollment);
        await db.SaveChangesAsync();

        // Act 1: Assign deployment
        await releaseService.AssignDeploymentAsync(deploymentId, enrollment.Id);

        // Verify initial state is Pending
        var state1 = await releaseService.GetDeviceDeploymentStateAsync(deploymentId, enrollment.Id);
        Assert.NotNull(state1);
        Assert.Equal(DeploymentDeviceStatus.Pending, state1.Status);

        // Act 2: Simulate device acknowledgement by updating state
        var updatedState = await releaseService.UpdateDeviceDeploymentStateAsync(
            deploymentId,
            enrollment.Id,
            DeploymentDeviceStatus.Downloading
        );

        // Assert 2: State transitioned to Downloading
        Assert.NotNull(updatedState);
        Assert.Equal(DeploymentDeviceStatus.Downloading, updatedState.Status);

        // Act 3: Further update to Installing
        var installingState = await releaseService.UpdateDeviceDeploymentStateAsync(
            deploymentId,
            enrollment.Id,
            DeploymentDeviceStatus.Installing
        );

        // Assert 3: State transitioned to Installing
        Assert.NotNull(installingState);
        Assert.Equal(DeploymentDeviceStatus.Installing, installingState.Status);

        // Act 4: Final transition to Completed
        var completedState = await releaseService.UpdateDeviceDeploymentStateAsync(
            deploymentId,
            enrollment.Id,
            DeploymentDeviceStatus.Completed
        );

        // Assert 4: Deployment complete
        Assert.NotNull(completedState);
        Assert.Equal(DeploymentDeviceStatus.Completed, completedState.Status);
    }

    [Fact]
    public async Task FullFlow_Deployment_StateTrackingErrorHandling()
    {
        // Arrange
        var dbContextFactory = new InMemoryDbContextFactory();
        var releaseService = new ReleaseService(dbContextFactory.CreateDbContext());

        using var db = dbContextFactory.CreateDbContext();

        // Create release
        var releaseId = await releaseService.CreateReleaseIdempotentAsync(
            ValidMetadataJson,
            "test-signature",
            "test-admin"
        );

        // Create deployment
        var deploymentId = await releaseService.CreatePilotDeploymentAsync(releaseId, "test-admin");

        // Create enrollment
        var enrollment = CreateEnrollment(db);
        db.Add(enrollment);
        await db.SaveChangesAsync();

        // Assign deployment
        await releaseService.AssignDeploymentAsync(deploymentId, enrollment.Id);

        // Act 1: Transition to Downloading
        await releaseService.UpdateDeviceDeploymentStateAsync(
            deploymentId,
            enrollment.Id,
            DeploymentDeviceStatus.Downloading
        );

        // Act 2: Simulate device error
        var errorState = await releaseService.UpdateDeviceDeploymentStateAsync(
            deploymentId,
            enrollment.Id,
            DeploymentDeviceStatus.Failed,
            "Device storage full: cannot install APK"
        );

        // Assert: Error state tracked with message
        Assert.NotNull(errorState);
        Assert.Equal(DeploymentDeviceStatus.Failed, errorState.Status);
        Assert.Equal("Device storage full: cannot install APK", errorState.ErrorMessage);
    }

    [Fact]
    public async Task FullFlow_MultipleDevices_Deployment()
    {
        // Arrange
        var dbContextFactory = new InMemoryDbContextFactory();
        var releaseService = new ReleaseService(dbContextFactory.CreateDbContext());
        var tagService = new DeviceTagService(dbContextFactory.CreateDbContext());
                var controller = new ReleaseController(releaseService, new BulkAssignmentService(dbContextFactory.CreateDbContext(), tagService, releaseService), null, dbContextFactory);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        using var db = dbContextFactory.CreateDbContext();

        // Create release
        var releaseId = await releaseService.CreateReleaseIdempotentAsync(
            ValidMetadataJson,
            "test-signature",
            "test-admin"
        );

        // Create deployment
        var deploymentId = await releaseService.CreatePilotDeploymentAsync(releaseId, "test-admin");

        // Create multiple enrollments
        var enrollments = new[]
        {
            CreateEnrollment(db),
            CreateEnrollment(db),
            CreateEnrollment(db)
        };

        foreach (var enrollment in enrollments)
        {
            db.Add(enrollment);
        }
        await db.SaveChangesAsync();

        // Act 1: Assign deployment to all devices
        foreach (var enrollment in enrollments)
        {
            var assignResult = await controller.AssignDeploymentAsync(
                deploymentId.ToString(),
                new AssignDeploymentRequest { EnrollmentId = enrollment.Id.ToString() }
            );
            Assert.IsType<OkObjectResult>(assignResult.Result);
        }

        // Act 2: Verify all devices are tracked
        using var dbCheck = dbContextFactory.CreateDbContext();
        var states = await dbCheck.DeploymentDeviceStates
            .Where(s => s.DeploymentId == deploymentId)
            .ToListAsync();

        // Assert: All 3 devices tracked
        Assert.Equal(3, states.Count);
        Assert.All(states, s => Assert.Equal(DeploymentDeviceStatus.Pending, s.Status));

        // Act 3: Update all devices to Downloading
        foreach (var state in states)
        {
            await releaseService.UpdateDeviceDeploymentStateAsync(
                state.DeploymentId,
                state.EnrollmentId,
                DeploymentDeviceStatus.Downloading
            );
        }

        // Act 4: Verify all updated
        using var dbFinal = dbContextFactory.CreateDbContext();
        var finalStates = await dbFinal.DeploymentDeviceStates
            .Where(s => s.DeploymentId == deploymentId)
            .ToListAsync();

        // Assert: All transitioned
        Assert.All(finalStates, s => Assert.Equal(DeploymentDeviceStatus.Downloading, s.Status));
    }

    private static PendingEnrollment CreateEnrollment(FleetDbContext db)
    {
        var tokens = new EnrollmentBootstrapTokens(TimeProvider.System);
        var issued = tokens.Issue(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32), TimeSpan.FromMinutes(5));
        var enrollment = PendingEnrollment.Create(issued.Credential);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var csr = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=test", key, HashAlgorithmName.SHA256).CreateSigningRequest();
        var verifiedKey = EnrollmentCsrVerifier.Verify(Convert.ToBase64String(csr));
        enrollment.Claim(verifiedKey, issued.Credential.IssuedAt);

        return enrollment;
    }

    private class InMemoryDbContextFactory : IDbContextFactory<FleetDbContext>
    {
        private readonly DbContextOptions<FleetDbContext> _options;

        public InMemoryDbContextFactory()
        {
            var builder = new DbContextOptionsBuilder<FleetDbContext>();
            builder.UseInMemoryDatabase($"integration-test-{Guid.NewGuid()}");
            _options = builder.Options;
        }

        public FleetDbContext CreateDbContext()
        {
            return new FleetDbContext(_options);
        }
    }
}
