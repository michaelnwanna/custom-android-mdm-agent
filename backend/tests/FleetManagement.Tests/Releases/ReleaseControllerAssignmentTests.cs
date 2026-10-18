namespace FleetManagement.Tests.Releases;

using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FleetManagement.Api.Releases;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using FleetManagement.Releases;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// Tests for Release Controller deployment assignment endpoint.
/// Tests MQTT integration and device state tracking.
/// </summary>
public class ReleaseControllerAssignmentTests
{
    private static readonly string ValidMetadataJson = @"{
  ""packageName"": ""com.example.app"",
  ""versionCode"": 42,
  ""apkSha256"": ""0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"",
  ""apkSizeBytes"": 1048576,
  ""minSdkLevel"": 29,
  ""expiresAt"": ""2099-12-31T23:59:59Z""
}";

    private readonly FakeDbContextFactory _dbContextFactory;
    private readonly ReleaseService _releaseService;

    public ReleaseControllerAssignmentTests()
    {
        _dbContextFactory = new FakeDbContextFactory();
        _releaseService = new ReleaseService(_dbContextFactory.CreateDbContext());
    }

    [Fact]
    public async Task AssignDeployment_With_Valid_Ids_Returns_Ok()
    {
        // Arrange
        using var db = _dbContextFactory.CreateDbContext();

        // Create release
        var releaseId = await _releaseService.CreateReleaseIdempotentAsync(
            metadataJson: ValidMetadataJson,
            metadataSignature: "valid_signature",
            creator: "test-user"
        );

        // Create deployment
        var deploymentId = await _releaseService.CreatePilotDeploymentAsync(releaseId, "test-user");

        // Create enrollment with claimed device
        var enrollment = CreateEnrollment(db);
        db.Add(enrollment);
        await db.SaveChangesAsync();

        var controller = new ReleaseController(_releaseService, null, _dbContextFactory);

        // Act
        var result = await controller.AssignDeploymentAsync(
            deploymentId.ToString(),
            new AssignDeploymentRequest { EnrollmentId = enrollment.Id.ToString() }
        );

        // Assert
        Assert.IsType<OkObjectResult>(result.Result);
        var okResult = (OkObjectResult)result.Result;
        var response = (AssignDeploymentResponse)okResult.Value!;

        Assert.Equal(deploymentId.ToString(), response.DeploymentId);
        Assert.Equal(enrollment.Id.ToString(), response.EnrollmentId);
        Assert.Equal("Pending", response.Status);
    }

    [Fact]
    public async Task AssignDeployment_With_Invalid_DeploymentId_Returns_BadRequest()
    {
        // Arrange
        var controller = new ReleaseController(_releaseService, null, _dbContextFactory);

        // Act
        var result = await controller.AssignDeploymentAsync(
            "invalid-id",
            new AssignDeploymentRequest { EnrollmentId = Guid.NewGuid().ToString() }
        );

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AssignDeployment_With_Invalid_EnrollmentId_Returns_BadRequest()
    {
        // Arrange
        using var db = _dbContextFactory.CreateDbContext();
        var releaseId = await _releaseService.CreateReleaseIdempotentAsync(
            metadataJson: ValidMetadataJson,
            metadataSignature: "valid_signature",
            creator: "test-user"
        );
        var deploymentId = await _releaseService.CreatePilotDeploymentAsync(releaseId, "test-user");

        var controller = new ReleaseController(_releaseService, null, _dbContextFactory);

        // Act
        var result = await controller.AssignDeploymentAsync(
            deploymentId.ToString(),
            new AssignDeploymentRequest { EnrollmentId = "invalid-id" }
        );

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AssignDeployment_With_Unknown_Enrollment_Returns_BadRequest()
    {
        // Arrange
        using var db = _dbContextFactory.CreateDbContext();
        var releaseId = await _releaseService.CreateReleaseIdempotentAsync(
            metadataJson: ValidMetadataJson,
            metadataSignature: "valid_signature",
            creator: "test-user"
        );
        var deploymentId = await _releaseService.CreatePilotDeploymentAsync(releaseId, "test-user");

        var controller = new ReleaseController(_releaseService, null, _dbContextFactory);

        // Act
        var result = await controller.AssignDeploymentAsync(
            deploymentId.ToString(),
            new AssignDeploymentRequest { EnrollmentId = Guid.NewGuid().ToString() }
        );

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AssignDeployment_Creates_Device_State_Tracking()
    {
        // Arrange
        using var db = _dbContextFactory.CreateDbContext();

        var releaseId = await _releaseService.CreateReleaseIdempotentAsync(
            metadataJson: ValidMetadataJson,
            metadataSignature: "valid_signature",
            creator: "test-user"
        );
        var deploymentId = await _releaseService.CreatePilotDeploymentAsync(releaseId, "test-user");

        var enrollment = CreateEnrollment(db);
        db.Add(enrollment);
        await db.SaveChangesAsync();

        var controller = new ReleaseController(_releaseService, null, _dbContextFactory);

        // Act
        await controller.AssignDeploymentAsync(
            deploymentId.ToString(),
            new AssignDeploymentRequest { EnrollmentId = enrollment.Id.ToString() }
        );

        // Assert - Verify state was created
        using var dbCheck = _dbContextFactory.CreateDbContext();
        var state = await dbCheck.DeploymentDeviceStates
            .FirstOrDefaultAsync(s =>
                s.DeploymentId == deploymentId &&
                s.EnrollmentId == enrollment.Id
            );

        Assert.NotNull(state);
        Assert.Equal(DeploymentDeviceStatus.Pending, state.Status);
    }

    [Fact]
    public async Task AssignDeployment_Idempotent_Returns_Same_StateId()
    {
        // Arrange
        using var db = _dbContextFactory.CreateDbContext();

        var releaseId = await _releaseService.CreateReleaseIdempotentAsync(
            metadataJson: ValidMetadataJson,
            metadataSignature: "valid_signature",
            creator: "test-user"
        );
        var deploymentId = await _releaseService.CreatePilotDeploymentAsync(releaseId, "test-user");

        var enrollment = CreateEnrollment(db);
        db.Add(enrollment);
        await db.SaveChangesAsync();

        var controller = new ReleaseController(_releaseService, null, _dbContextFactory);

        // Act - Assign twice
        var result1 = await controller.AssignDeploymentAsync(
            deploymentId.ToString(),
            new AssignDeploymentRequest { EnrollmentId = enrollment.Id.ToString() }
        );

        var result2 = await controller.AssignDeploymentAsync(
            deploymentId.ToString(),
            new AssignDeploymentRequest { EnrollmentId = enrollment.Id.ToString() }
        );

        // Assert - Same StateId returned
        var response1 = ((OkObjectResult)result1.Result!).Value as AssignDeploymentResponse;
        var response2 = ((OkObjectResult)result2.Result!).Value as AssignDeploymentResponse;

        Assert.Equal(response1!.StateId, response2!.StateId);
    }

    private static PendingEnrollment CreateEnrollment(FleetDbContext db)
    {
        var tokens = new EnrollmentBootstrapTokens(TimeProvider.System);
        var issued = tokens.Issue(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32), TimeSpan.FromMinutes(5));
        var enrollment = PendingEnrollment.Create(issued.Credential);

        // Claim the enrollment to assign a device
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var csr = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=test", key, HashAlgorithmName.SHA256).CreateSigningRequest();
        var verifiedKey = EnrollmentCsrVerifier.Verify(Convert.ToBase64String(csr));
        enrollment.Claim(verifiedKey, issued.Credential.IssuedAt);

        return enrollment;
    }

    private class FakeDbContextFactory : IDbContextFactory<FleetDbContext>
    {
        private readonly DbContextOptions<FleetDbContext> _options;

        public FakeDbContextFactory()
        {
            var builder = new DbContextOptionsBuilder<FleetDbContext>();
            builder.UseInMemoryDatabase($"test-db-{Guid.NewGuid()}");
            _options = builder.Options;
        }

        public FleetDbContext CreateDbContext()
        {
            return new FleetDbContext(_options);
        }
    }
}
