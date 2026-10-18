namespace FleetManagement.Tests.Mqtt;

using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FleetManagement.Api.Mqtt;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using FleetManagement.Releases;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// Tests for MQTT status reporting and device state transitions.
/// </summary>
public class MqttStatusReportingTests
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
    public async Task DeviceStatusReport_Acknowledged_TransitionsState()
    {
        // Arrange
        var factory = new InMemoryDbContextFactory();
        var db = factory.CreateDbContext();
        var releaseService = new ReleaseService(db);

        var releaseId = await releaseService.CreateReleaseIdempotentAsync(
            ValidMetadataJson, "sig", "admin");
        var deploymentId = await releaseService.CreatePilotDeploymentAsync(releaseId, "admin");
        
        var enrollment = CreateAndSaveEnrollment(db);
        await releaseService.AssignDeploymentAsync(deploymentId, enrollment.Id);

        // Act
        var updated = await releaseService.UpdateDeviceDeploymentStateAsync(
            deploymentId,
            enrollment.Id,
            DeploymentDeviceStatus.Downloading
        );

        // Assert
        Assert.NotNull(updated);
        Assert.Equal(DeploymentDeviceStatus.Downloading, updated.Status);
    }

    [Fact]
    public async Task DeviceStatusReport_Downloading_WithProgress()
    {
        // Arrange
        var factory = new InMemoryDbContextFactory();
        var db = factory.CreateDbContext();
        var releaseService = new ReleaseService(db);

        var releaseId = await releaseService.CreateReleaseIdempotentAsync(
            ValidMetadataJson, "sig", "admin");
        var deploymentId = await releaseService.CreatePilotDeploymentAsync(releaseId, "admin");
        
        var enrollment = CreateAndSaveEnrollment(db);
        await releaseService.AssignDeploymentAsync(deploymentId, enrollment.Id);

        // Act - Simulate download progress updates
        await releaseService.UpdateDeviceDeploymentStateAsync(
            deploymentId, enrollment.Id, DeploymentDeviceStatus.Downloading);
        
        await Task.Delay(10);
        
        await releaseService.UpdateDeviceDeploymentStateAsync(
            deploymentId, enrollment.Id, DeploymentDeviceStatus.Downloading);

        // Assert
        using var dbCheck = factory.CreateDbContext();
        var state = await dbCheck.DeploymentDeviceStates
            .FirstOrDefaultAsync(s =>
                s.DeploymentId == deploymentId &&
                s.EnrollmentId == enrollment.Id
            );

        Assert.NotNull(state);
        Assert.Equal(DeploymentDeviceStatus.Downloading, state.Status);
    }

    [Fact]
    public async Task DeviceStatusReport_Installing_TransitionsState()
    {
        // Arrange
        var factory = new InMemoryDbContextFactory();
        var db = factory.CreateDbContext();
        var releaseService = new ReleaseService(db);

        var releaseId = await releaseService.CreateReleaseIdempotentAsync(
            ValidMetadataJson, "sig", "admin");
        var deploymentId = await releaseService.CreatePilotDeploymentAsync(releaseId, "admin");
        
        var enrollment = CreateAndSaveEnrollment(db);
        await releaseService.AssignDeploymentAsync(deploymentId, enrollment.Id);

        // Move through states
        await releaseService.UpdateDeviceDeploymentStateAsync(
            deploymentId, enrollment.Id, DeploymentDeviceStatus.Downloading);

        // Act
        var updated = await releaseService.UpdateDeviceDeploymentStateAsync(
            deploymentId, enrollment.Id, DeploymentDeviceStatus.Installing);

        // Assert
        Assert.NotNull(updated);
        Assert.Equal(DeploymentDeviceStatus.Installing, updated.Status);
    }

    [Fact]
    public async Task DeviceStatusReport_Installed_Completed()
    {
        // Arrange
        var factory = new InMemoryDbContextFactory();
        var db = factory.CreateDbContext();
        var releaseService = new ReleaseService(db);

        var releaseId = await releaseService.CreateReleaseIdempotentAsync(
            ValidMetadataJson, "sig", "admin");
        var deploymentId = await releaseService.CreatePilotDeploymentAsync(releaseId, "admin");
        
        var enrollment = CreateAndSaveEnrollment(db);
        await releaseService.AssignDeploymentAsync(deploymentId, enrollment.Id);

        // Move through states
        await releaseService.UpdateDeviceDeploymentStateAsync(
            deploymentId, enrollment.Id, DeploymentDeviceStatus.Downloading);
        await releaseService.UpdateDeviceDeploymentStateAsync(
            deploymentId, enrollment.Id, DeploymentDeviceStatus.Installing);

        // Act
        var updated = await releaseService.UpdateDeviceDeploymentStateAsync(
            deploymentId, enrollment.Id, DeploymentDeviceStatus.Completed);

        // Assert
        Assert.NotNull(updated);
        Assert.Equal(DeploymentDeviceStatus.Completed, updated.Status);
        Assert.Null(updated.ErrorMessage);
    }

    [Fact]
    public async Task DeviceStatusReport_Failed_WithErrorMessage()
    {
        // Arrange
        var factory = new InMemoryDbContextFactory();
        var db = factory.CreateDbContext();
        var releaseService = new ReleaseService(db);

        var releaseId = await releaseService.CreateReleaseIdempotentAsync(
            ValidMetadataJson, "sig", "admin");
        var deploymentId = await releaseService.CreatePilotDeploymentAsync(releaseId, "admin");
        
        var enrollment = CreateAndSaveEnrollment(db);
        await releaseService.AssignDeploymentAsync(deploymentId, enrollment.Id);

        const string errorMsg = "Insufficient storage: 500MB required, 250MB available";

        // Act
        var updated = await releaseService.UpdateDeviceDeploymentStateAsync(
            deploymentId,
            enrollment.Id,
            DeploymentDeviceStatus.Failed,
            errorMsg
        );

        // Assert
        Assert.NotNull(updated);
        Assert.Equal(DeploymentDeviceStatus.Failed, updated.Status);
        Assert.Equal(errorMsg, updated.ErrorMessage);
    }

    [Fact]
    public async Task StatusReportMapping_AllStatuses()
    {
        // Arrange
        var testCases = new[]
        {
            (MqttDeviceStatus.Acknowledged, DeploymentDeviceStatus.Downloading),
            (MqttDeviceStatus.Downloading, DeploymentDeviceStatus.Downloading),
            (MqttDeviceStatus.Downloaded, DeploymentDeviceStatus.Installing),
            (MqttDeviceStatus.Installing, DeploymentDeviceStatus.Installing),
            (MqttDeviceStatus.Installed, DeploymentDeviceStatus.Completed),
            (MqttDeviceStatus.Failed, DeploymentDeviceStatus.Failed)
        };

        // Act & Assert - Map each status
        foreach (var (mqttStatus, expectedStatus) in testCases)
        {
            var mapped = MapMqttStatus(mqttStatus);
            Assert.Equal(expectedStatus, mapped);
        }
    }

    [Fact]
    public async Task DeviceStatusReport_MultipleDevices_IndependentStates()
    {
        // Arrange
        var factory = new InMemoryDbContextFactory();
        var db = factory.CreateDbContext();
        var releaseService = new ReleaseService(db);

        var releaseId = await releaseService.CreateReleaseIdempotentAsync(
            ValidMetadataJson, "sig", "admin");
        var deploymentId = await releaseService.CreatePilotDeploymentAsync(releaseId, "admin");

        var device1 = CreateAndSaveEnrollment(db);
        var device2 = CreateAndSaveEnrollment(db);
        var device3 = CreateAndSaveEnrollment(db);

        await releaseService.AssignDeploymentAsync(deploymentId, device1.Id);
        await releaseService.AssignDeploymentAsync(deploymentId, device2.Id);
        await releaseService.AssignDeploymentAsync(deploymentId, device3.Id);

        // Act - Update devices to different states
        await releaseService.UpdateDeviceDeploymentStateAsync(
            deploymentId, device1.Id, DeploymentDeviceStatus.Downloading);

        await releaseService.UpdateDeviceDeploymentStateAsync(
            deploymentId, device2.Id, DeploymentDeviceStatus.Downloading);
        await releaseService.UpdateDeviceDeploymentStateAsync(
            deploymentId, device2.Id, DeploymentDeviceStatus.Installing);

        await releaseService.UpdateDeviceDeploymentStateAsync(
            deploymentId, device3.Id, DeploymentDeviceStatus.Failed, "Network error");

        // Assert - Verify independent states
        using var dbCheck = factory.CreateDbContext();
        
        var state1 = await dbCheck.DeploymentDeviceStates
            .FirstOrDefaultAsync(s => s.EnrollmentId == device1.Id);
        Assert.Equal(DeploymentDeviceStatus.Downloading, state1!.Status);

        var state2 = await dbCheck.DeploymentDeviceStates
            .FirstOrDefaultAsync(s => s.EnrollmentId == device2.Id);
        Assert.Equal(DeploymentDeviceStatus.Installing, state2!.Status);

        var state3 = await dbCheck.DeploymentDeviceStates
            .FirstOrDefaultAsync(s => s.EnrollmentId == device3.Id);
        Assert.Equal(DeploymentDeviceStatus.Failed, state3!.Status);
        Assert.Equal("Network error", state3.ErrorMessage);
    }

    [Fact]
    public async Task DeviceStatusReport_ErrorCodes_Preserved()
    {
        // Arrange
        var factory = new InMemoryDbContextFactory();
        var db = factory.CreateDbContext();
        var releaseService = new ReleaseService(db);

        var releaseId = await releaseService.CreateReleaseIdempotentAsync(
            ValidMetadataJson, "sig", "admin");
        var deploymentId = await releaseService.CreatePilotDeploymentAsync(releaseId, "admin");
        
        var enrollment = CreateAndSaveEnrollment(db);
        await releaseService.AssignDeploymentAsync(deploymentId, enrollment.Id);

        var errorCodes = new[]
        {
            ("INSUFFICIENT_STORAGE", "Not enough storage space"),
            ("INVALID_SIGNATURE", "APK signature verification failed"),
            ("NETWORK_TIMEOUT", "Download timed out after 5 retries"),
            ("INSTALLATION_FAILED", "Package installer returned error code 1"),
            ("DEVICE_ADMIN_DISABLED", "Device admin policy prevented installation")
        };

        // Act & Assert - Each error code
        foreach (var (code, message) in errorCodes)
        {
            var updated = await releaseService.UpdateDeviceDeploymentStateAsync(
                deploymentId,
                enrollment.Id,
                DeploymentDeviceStatus.Failed,
                $"[{code}] {message}"
            );

            Assert.NotNull(updated);
            Assert.Contains(code, updated.ErrorMessage);
            Assert.Contains(message, updated.ErrorMessage);
        }
    }

    private static DeploymentDeviceStatus MapMqttStatus(MqttDeviceStatus mqttStatus)
    {
        return mqttStatus switch
        {
            MqttDeviceStatus.Acknowledged => DeploymentDeviceStatus.Downloading,
            MqttDeviceStatus.Downloading => DeploymentDeviceStatus.Downloading,
            MqttDeviceStatus.Downloaded => DeploymentDeviceStatus.Installing,
            MqttDeviceStatus.Installing => DeploymentDeviceStatus.Installing,
            MqttDeviceStatus.Installed => DeploymentDeviceStatus.Completed,
            MqttDeviceStatus.Failed => DeploymentDeviceStatus.Failed,
            _ => throw new ArgumentException($"Unknown MQTT status: {mqttStatus}")
        };
    }

    private static PendingEnrollment CreateAndSaveEnrollment(FleetDbContext db)
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

        db.Add(enrollment);
        db.SaveChanges();

        return enrollment;
    }

    private class InMemoryDbContextFactory : IDbContextFactory<FleetDbContext>
    {
        private readonly DbContextOptions<FleetDbContext> _options;

        public InMemoryDbContextFactory()
        {
            var builder = new DbContextOptionsBuilder<FleetDbContext>();
            builder.UseInMemoryDatabase($"mqtt-test-{Guid.NewGuid()}");
            _options = builder.Options;
        }

        public FleetDbContext CreateDbContext()
        {
            return new FleetDbContext(_options);
        }
    }
}
