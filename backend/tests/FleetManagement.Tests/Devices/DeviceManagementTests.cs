namespace FleetManagement.Tests.Devices;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FleetManagement.Devices;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class DeviceTagServiceTests : IAsyncLifetime
{
    private readonly DbContextOptions<FleetDbContext> _options;
    private FleetDbContext _db = null!;
    private DeviceTagService _service = null!;

    public DeviceTagServiceTests()
    {
        _options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    public async Task InitializeAsync()
    {
        _db = new FleetDbContext(_options);
        await _db.Database.EnsureCreatedAsync();
        _service = new DeviceTagService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        _db.Dispose();
    }

    [Fact]
    public async Task CreateTag_WithValidData_SucceedsAndNormalizesName()
    {
        // Act
        var tag = await _service.CreateTagAsync("  Production  ", "admin", "Production devices");

        // Assert
        Assert.NotNull(tag);
        Assert.Equal("production", tag.Name);
        Assert.Equal("Production devices", tag.Description);
        Assert.True(tag.IsActive);
        Assert.Equal("admin", tag.CreatedBy);
    }

    [Fact]
    public async Task CreateTag_WithDuplicateName_ThrowsInvalidOperationException()
    {
        // Arrange
        await _service.CreateTagAsync("production", "admin");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateTagAsync("PRODUCTION", "admin"));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task CreateTag_WithEmptyName_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.CreateTagAsync("  ", "admin"));
    }

    [Fact]
    public async Task GetTagByName_WithExistingTag_ReturnsTag()
    {
        // Arrange
        await _service.CreateTagAsync("production", "admin");

        // Act
        var tag = await _service.GetTagByNameAsync("PRODUCTION");

        // Assert
        Assert.NotNull(tag);
        Assert.Equal("production", tag.Name);
    }

    [Fact]
    public async Task GetTagByName_WithNonExistentTag_ReturnsNull()
    {
        // Act
        var tag = await _service.GetTagByNameAsync("nonexistent");

        // Assert
        Assert.Null(tag);
    }

    [Fact]
    public async Task GetTags_WithMultipleTags_ReturnsSortedByName()
    {
        // Arrange
        await _service.CreateTagAsync("zebra", "admin");
        await _service.CreateTagAsync("alpha", "admin");
        await _service.CreateTagAsync("beta", "admin");

        // Act
        var tags = await _service.GetTagsAsync();

        // Assert
        Assert.Equal(3, tags.Count);
        Assert.Equal(new[] { "alpha", "beta", "zebra" }, tags.Select(t => t.Name));
    }

    [Fact]
    public async Task GetTags_WithOnlyActive_FiltersInactiveTags()
    {
        // Arrange
        var active = await _service.CreateTagAsync("active", "admin");
        var inactive = await _service.CreateTagAsync("inactive", "admin");
        await _service.UpdateTagAsync(inactive.TagId, null, false);

        // Act
        var tags = await _service.GetTagsAsync(onlyActive: true);

        // Assert
        Assert.Single(tags);
        Assert.Equal("active", tags[0].Name);
    }

    [Fact]
    public async Task UpdateTag_WithValidData_UpdatesDescriptionAndActiveStatus()
    {
        // Arrange
        var tag = await _service.CreateTagAsync("test", "admin", "Old description");

        // Act
        await _service.UpdateTagAsync(tag.TagId, "New description", false);

        // Assert
        var updated = await _service.GetTagAsync(tag.TagId);
        Assert.Equal("New description", updated!.Description);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task AssignTag_WithValidDeviceAndTag_CreatesAssignment()
    {
        // Arrange
        var device = CreateAndSaveDevice();
        var tag = await _service.CreateTagAsync("test", "admin");

        // Act
        await _service.AssignTagAsync(device.DeviceId, tag.TagId, "admin");

        // Assert
        var tags = await _service.GetDeviceTagsAsync(device.DeviceId);
        Assert.Single(tags);
        Assert.Equal(tag.TagId, tags[0].TagId);
    }

    [Fact]
    public async Task AssignTag_WithNonExistentDevice_ThrowsKeyNotFoundException()
    {
        // Arrange
        var tag = await _service.CreateTagAsync("test", "admin");

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.AssignTagAsync(Guid.NewGuid(), tag.TagId, "admin"));
    }

    [Fact]
    public async Task AssignTag_WithNonExistentTag_ThrowsKeyNotFoundException()
    {
        // Arrange
        var device = CreateAndSaveDevice();

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.AssignTagAsync(device.DeviceId, Guid.NewGuid(), "admin"));
    }

    [Fact]
    public async Task AssignTag_WithDuplicateAssignment_ThrowsInvalidOperationException()
    {
        // Arrange
        var device = CreateAndSaveDevice();
        var tag = await _service.CreateTagAsync("test", "admin");
        await _service.AssignTagAsync(device.DeviceId, tag.TagId, "admin");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.AssignTagAsync(device.DeviceId, tag.TagId, "admin"));
        Assert.Contains("already assigned", ex.Message);
    }

    [Fact]
    public async Task RemoveTag_WithValidAssignment_MarksAsRemoved()
    {
        // Arrange
        var device = CreateAndSaveDevice();
        var tag = await _service.CreateTagAsync("test", "admin");
        await _service.AssignTagAsync(device.DeviceId, tag.TagId, "admin");

        // Act
        await _service.RemoveTagAsync(device.DeviceId, tag.TagId, "admin");

        // Assert
        var tags = await _service.GetDeviceTagsAsync(device.DeviceId);
        Assert.Empty(tags);
    }

    [Fact]
    public async Task RemoveTag_WithNonExistentAssignment_ThrowsKeyNotFoundException()
    {
        // Arrange
        var device = CreateAndSaveDevice();
        var tag = await _service.CreateTagAsync("test", "admin");

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _service.RemoveTagAsync(device.DeviceId, tag.TagId, "admin"));
    }

    [Fact]
    public async Task GetDevicesWithTag_WithValidTag_ReturnsAssignedDevices()
    {
        // Arrange
        var tag = await _service.CreateTagAsync("test", "admin");
        var device1 = CreateAndSaveDevice();
        var device2 = CreateAndSaveDevice();
        var device3 = CreateAndSaveDevice();
        
        await _service.AssignTagAsync(device1.DeviceId, tag.TagId, "admin");
        await _service.AssignTagAsync(device2.DeviceId, tag.TagId, "admin");

        // Act
        var devices = await _service.GetDevicesWithTagAsync(tag.TagId);

        // Assert
        Assert.Equal(2, devices.Count);
        Assert.Contains(device1.DeviceId, devices.Select(d => d.DeviceId));
        Assert.Contains(device2.DeviceId, devices.Select(d => d.DeviceId));
        Assert.DoesNotContain(device3.DeviceId, devices.Select(d => d.DeviceId));
    }

    [Fact]
    public async Task GetDevicesWithTagByName_WithValidTagName_ReturnsAssignedDevices()
    {
        // Arrange
        var tag = await _service.CreateTagAsync("production", "admin");
        var device = CreateAndSaveDevice();
        await _service.AssignTagAsync(device.DeviceId, tag.TagId, "admin");

        // Act
        var devices = await _service.GetDevicesWithTagByNameAsync("PRODUCTION");

        // Assert
        Assert.Single(devices);
        Assert.Equal(device.DeviceId, devices[0].DeviceId);
    }

    [Fact]
    public async Task GetDevicesWithAnyTag_WithValidTags_ReturnsDevicesWithAtLeastOneTag()
    {
        // Arrange
        var tag1 = await _service.CreateTagAsync("tag1", "admin");
        var tag2 = await _service.CreateTagAsync("tag2", "admin");
        var device1 = CreateAndSaveDevice();
        var device2 = CreateAndSaveDevice();
        var device3 = CreateAndSaveDevice();

        await _service.AssignTagAsync(device1.DeviceId, tag1.TagId, "admin");
        await _service.AssignTagAsync(device2.DeviceId, tag2.TagId, "admin");

        // Act
        var devices = await _service.GetDevicesWithAnyTagAsync(new[] { tag1.TagId, tag2.TagId });

        // Assert
        Assert.Equal(2, devices.Count);
        Assert.Contains(device1.DeviceId, devices.Select(d => d.DeviceId));
        Assert.Contains(device2.DeviceId, devices.Select(d => d.DeviceId));
        Assert.DoesNotContain(device3.DeviceId, devices.Select(d => d.DeviceId));
    }

    [Fact]
    public async Task GetDevicesWithAllTags_WithValidTags_ReturnsDevicesWithAllTags()
    {
        // Arrange
        var tag1 = await _service.CreateTagAsync("tag1", "admin");
        var tag2 = await _service.CreateTagAsync("tag2", "admin");
        var device1 = CreateAndSaveDevice();
        var device2 = CreateAndSaveDevice();
        var device3 = CreateAndSaveDevice();

        await _service.AssignTagAsync(device1.DeviceId, tag1.TagId, "admin");
        await _service.AssignTagAsync(device1.DeviceId, tag2.TagId, "admin");
        await _service.AssignTagAsync(device2.DeviceId, tag1.TagId, "admin");

        // Act
        var devices = await _service.GetDevicesWithAllTagsAsync(new[] { tag1.TagId, tag2.TagId });

        // Assert
        Assert.Single(devices);
        Assert.Equal(device1.DeviceId, devices[0].DeviceId);
    }

    [Fact]
    public async Task GetAssignmentHistory_WithRemovedAssignment_IncludesRemovedRecord()
    {
        // Arrange
        var device = CreateAndSaveDevice();
        var tag = await _service.CreateTagAsync("test", "admin");
        await _service.AssignTagAsync(device.DeviceId, tag.TagId, "admin");
        await _service.RemoveTagAsync(device.DeviceId, tag.TagId, "admin");

        // Act
        var history = await _service.GetAssignmentHistoryAsync(device.DeviceId, tag.TagId);

        // Assert
        Assert.Single(history);
        Assert.NotNull(history[0].RemovedAt);
        Assert.Equal("admin", history[0].RemovedBy);
    }

    private Device CreateAndSaveDevice()
    {
        var enrollment = CreateAndSaveClaimedEnrollment();
        var device = Device.Create(enrollment.DeviceId!.Value, enrollment.Id);
        _db.Devices.Add(device);
        _db.SaveChanges();
        return device;
    }

    private PendingEnrollment CreateAndSaveClaimedEnrollment()
    {
        var tokens = new EnrollmentBootstrapTokens(TimeProvider.System);
        var issued = tokens.Issue(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32), TimeSpan.FromMinutes(15));
        var enrollment = PendingEnrollment.Create(issued.Credential);

        // Claim the enrollment to assign a device
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var csr = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=test", key, HashAlgorithmName.SHA256).CreateSigningRequest();
        var verifiedKey = EnrollmentCsrVerifier.Verify(Convert.ToBase64String(csr));
        enrollment.Claim(verifiedKey, issued.Credential.IssuedAt);

        _db.PendingEnrollments.Add(enrollment);
        _db.SaveChanges();
        return enrollment;
    }
}

public class DeviceServiceTests : IAsyncLifetime
{
    private readonly DbContextOptions<FleetDbContext> _options;
    private FleetDbContext _db = null!;
    private DeviceService _service = null!;

    public DeviceServiceTests()
    {
        _options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    public async Task InitializeAsync()
    {
        _db = new FleetDbContext(_options);
        await _db.Database.EnsureCreatedAsync();
        _service = new DeviceService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        _db.Dispose();
    }

    [Fact]
    public async Task CreateDeviceFromEnrollment_WithClaimedEnrollment_CreatesDevice()
    {
        // Arrange
        var enrollment = CreateAndSaveClaimedEnrollment();

        // Act
        var device = await _service.CreateDeviceFromEnrollmentAsync(enrollment.Id);

        // Assert
        Assert.NotNull(device);
        Assert.Equal(enrollment.DeviceId!.Value, device.DeviceId);
        Assert.Equal(enrollment.Id, device.EnrollmentId);
        Assert.Equal(DeviceState.Active, device.State);
    }

    [Fact]
    public async Task CreateDeviceFromEnrollment_WithUnclaimedEnrollment_ThrowsInvalidOperationException()
    {
        // Arrange
        var tokens = new EnrollmentBootstrapTokens(TimeProvider.System);
        var issued = tokens.Issue(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32), TimeSpan.FromMinutes(15));
        var enrollment = PendingEnrollment.Create(issued.Credential);
        
        _db.PendingEnrollments.Add(enrollment);
        await _db.SaveChangesAsync();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateDeviceFromEnrollmentAsync(enrollment.Id));
        Assert.Contains("not been claimed", ex.Message);
    }

    [Fact]
    public async Task CreateDeviceFromEnrollment_WithAlreadyCreatedDevice_ThrowsInvalidOperationException()
    {
        // Arrange
        var enrollment = CreateAndSaveClaimedEnrollment();
        await _service.CreateDeviceFromEnrollmentAsync(enrollment.Id);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CreateDeviceFromEnrollmentAsync(enrollment.Id));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task GetDevice_WithExistingDevice_ReturnsDevice()
    {
        // Arrange
        var device = await _service.CreateDeviceFromEnrollmentAsync(CreateAndSaveClaimedEnrollment().Id);

        // Act
        var retrieved = await _service.GetDeviceAsync(device.DeviceId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(device.DeviceId, retrieved.DeviceId);
    }

    [Fact]
    public async Task GetDevice_WithNonExistentDevice_ReturnsNull()
    {
        // Act
        var device = await _service.GetDeviceAsync(Guid.NewGuid());

        // Assert
        Assert.Null(device);
    }

    [Fact]
    public async Task GetDeviceByEnrollment_WithExistingEnrollment_ReturnsDevice()
    {
        // Arrange
        var enrollment = CreateAndSaveClaimedEnrollment();
        var device = await _service.CreateDeviceFromEnrollmentAsync(enrollment.Id);

        // Act
        var retrieved = await _service.GetDeviceByEnrollmentAsync(enrollment.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(device.DeviceId, retrieved.DeviceId);
    }

    [Fact]
    public async Task UpdateDeviceProfile_WithValidData_UpdatesMetadata()
    {
        // Arrange
        var device = await _service.CreateDeviceFromEnrollmentAsync(CreateAndSaveClaimedEnrollment().Id);

        // Act
        await _service.UpdateDeviceProfileAsync(device.DeviceId, "phone", "Samsung", "Galaxy S21", 13);

        // Assert
        var updated = await _service.GetDeviceAsync(device.DeviceId);
        Assert.Equal("phone", updated!.DeviceType);
        Assert.Equal("Samsung", updated.Manufacturer);
        Assert.Equal("Galaxy S21", updated.Model);
        Assert.Equal(13, updated.OsVersion);
    }

    [Fact]
    public async Task UpdateLastSeen_WithExistingDevice_UpdatesTimestamp()
    {
        // Arrange
        var device = await _service.CreateDeviceFromEnrollmentAsync(CreateAndSaveClaimedEnrollment().Id);
        var originalLastSeen = device.LastSeenAt;
        await Task.Delay(10);

        // Act
        await _service.UpdateLastSeenAsync(device.DeviceId);

        // Assert
        var updated = await _service.GetDeviceAsync(device.DeviceId);
        Assert.True(updated!.LastSeenAt > originalLastSeen);
    }

    [Fact]
    public async Task SetAccessDisabled_WithTrue_DisablesAccess()
    {
        // Arrange
        var device = await _service.CreateDeviceFromEnrollmentAsync(CreateAndSaveClaimedEnrollment().Id);

        // Act
        await _service.SetAccessDisabledAsync(device.DeviceId, true);

        // Assert
        var updated = await _service.GetDeviceAsync(device.DeviceId);
        Assert.True(updated!.AccessDisabled);
    }

    [Fact]
    public async Task TransitionDeviceState_WithValidState_UpdatesState()
    {
        // Arrange
        var device = await _service.CreateDeviceFromEnrollmentAsync(CreateAndSaveClaimedEnrollment().Id);

        // Act
        await _service.TransitionDeviceStateAsync(device.DeviceId, DeviceState.Retired);

        // Assert
        var updated = await _service.GetDeviceAsync(device.DeviceId);
        Assert.Equal(DeviceState.Retired, updated!.State);
    }

    [Fact]
    public async Task GetActiveDevices_WithMixedStates_ReturnsOnlyActiveDevices()
    {
        // Arrange
        var active1 = await _service.CreateDeviceFromEnrollmentAsync(CreateAndSaveClaimedEnrollment().Id);
        var active2 = await _service.CreateDeviceFromEnrollmentAsync(CreateAndSaveClaimedEnrollment().Id);
        var retired = await _service.CreateDeviceFromEnrollmentAsync(CreateAndSaveClaimedEnrollment().Id);
        
        await _service.TransitionDeviceStateAsync(retired.DeviceId, DeviceState.Retired);

        // Act
        var devices = await _service.GetActiveDevicesAsync();

        // Assert
        Assert.Equal(2, devices.Count);
        Assert.DoesNotContain(retired.DeviceId, devices.Select(d => d.DeviceId));
    }

    [Fact]
    public async Task GetDeviceCountByState_WithMixedStates_ReturnsCorrectCounts()
    {
        // Arrange
        await _service.CreateDeviceFromEnrollmentAsync(CreateAndSaveClaimedEnrollment().Id);
        await _service.CreateDeviceFromEnrollmentAsync(CreateAndSaveClaimedEnrollment().Id);
        var retired = await _service.CreateDeviceFromEnrollmentAsync(CreateAndSaveClaimedEnrollment().Id);
        
        await _service.TransitionDeviceStateAsync(retired.DeviceId, DeviceState.Retired);

        // Act
        var counts = await _service.GetDeviceCountByStateAsync();

        // Assert
        Assert.Equal(2, counts[DeviceState.Active]);
        Assert.Equal(1, counts[DeviceState.Retired]);
    }

    private PendingEnrollment CreateAndSaveClaimedEnrollment()
    {
        var tokens = new EnrollmentBootstrapTokens(TimeProvider.System);
        var issued = tokens.Issue(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32), TimeSpan.FromMinutes(15));
        var enrollment = PendingEnrollment.Create(issued.Credential);

        // Claim the enrollment to assign a device
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var csr = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=test", key, HashAlgorithmName.SHA256).CreateSigningRequest();
        var verifiedKey = EnrollmentCsrVerifier.Verify(Convert.ToBase64String(csr));
        enrollment.Claim(verifiedKey, issued.Credential.IssuedAt);

        _db.PendingEnrollments.Add(enrollment);
        _db.SaveChanges();
        return enrollment;
    }
}
