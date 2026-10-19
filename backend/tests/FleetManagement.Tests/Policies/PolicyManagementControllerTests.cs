namespace FleetManagement.Tests.Policies;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FleetManagement.Api.Controllers;
using FleetManagement.Devices;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using FleetManagement.Policies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class PolicyManagementControllerTests : IAsyncLifetime
{
    private readonly DbContextOptions<FleetDbContext> _options =
        new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private FleetDbContext _db = null!;
    private PolicyController _controller = null!;

    public async Task InitializeAsync()
    {
        _db = new FleetDbContext(_options);
        await _db.Database.EnsureCreatedAsync();
        _controller = new PolicyController(
            new PolicyService(_db),
            null!,
            _db);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task ComplianceReportUsesPolicyAcknowledgementAndEnrollmentData()
    {
        var compliantEnrollment = await AddDeviceAsync("compliant-device");
        var pendingEnrollment = await AddDeviceAsync("pending-device");
        var nonCompliantEnrollment = await AddDeviceAsync("non-compliant-device");
        await AddDeviceAsync("unassigned-device");
        var policy = new Policy
        {
            PolicyId = Guid.NewGuid(),
            Creator = "admin",
            PolicyJson = """{"name":"Security","type":"Security"}"""
        };
        _db.Policies.Add(policy);
        _db.PolicyAssignments.AddRange(
            new PolicyAssignment
            {
                AssignmentId = Guid.NewGuid(),
                EnrollmentId = compliantEnrollment,
                PolicyId = policy.PolicyId,
                AssignedVersion = 2,
                AcknowledgedVersion = 2,
                AssignedAt = DateTimeOffset.UtcNow
            },
            new PolicyAssignment
            {
                AssignmentId = Guid.NewGuid(),
                EnrollmentId = pendingEnrollment,
                PolicyId = policy.PolicyId,
                AssignedVersion = 2,
                AssignedAt = DateTimeOffset.UtcNow
            },
            new PolicyAssignment
            {
                AssignmentId = Guid.NewGuid(),
                EnrollmentId = nonCompliantEnrollment,
                PolicyId = policy.PolicyId,
                AssignedVersion = 2,
                AcknowledgedVersion = 1,
                AssignedAt = DateTimeOffset.UtcNow
            });
        await _db.SaveChangesAsync();

        var action = await _controller.GetComplianceReport();
        var response = Assert.IsType<OkObjectResult>(action.Result);
        var devices = Assert.IsAssignableFrom<IReadOnlyList<PolicyManagementDeviceResponse>>(response.Value);

        Assert.Equal(4, devices.Count);
        Assert.Equal("compliant", devices.Single(device => device.EnrollmentId == compliantEnrollment).ComplianceStatus);
        Assert.Equal("pending", devices.Single(device => device.EnrollmentId == pendingEnrollment).ComplianceStatus);
        Assert.Equal("non-compliant", devices.Single(device => device.EnrollmentId == nonCompliantEnrollment).ComplianceStatus);
        Assert.Equal("unassigned", devices.Single(device => device.DeviceName == "Test unassigned-device").ComplianceStatus);
    }

    [Fact]
    public async Task AssignableDevicesExcludeRetiredAndAccessDisabledDevices()
    {
        await AddDeviceAsync("active-device");
        await AddDeviceAsync("retired-device", DeviceState.Retired);
        await AddDeviceAsync("disabled-device", accessDisabled: true);

        var action = await _controller.GetAssignableDevices();
        var response = Assert.IsType<OkObjectResult>(action.Result);
        var devices = Assert.IsAssignableFrom<IReadOnlyList<PolicyManagementDeviceResponse>>(response.Value);

        var device = Assert.Single(devices);
        Assert.Equal("Test active-device", device.DeviceName);
    }

    [Fact]
    public async Task PolicyListReturnsMetadataFromStoredJson()
    {
        _db.Policies.Add(new Policy
        {
            PolicyId = Guid.NewGuid(),
            Creator = "admin",
            PolicyJson = """{"name":"Kiosk","description":"Restricted devices","type":"Security"}"""
        });
        await _db.SaveChangesAsync();

        var action = await _controller.List();
        var response = Assert.IsType<OkObjectResult>(action.Result);
        var policies = Assert.IsType<ManagedPoliciesResponse>(response.Value);

        var policy = Assert.Single(policies.Items);
        Assert.Equal("Kiosk", policy.Name);
        Assert.Equal("Security", policy.Type);
        Assert.Equal(1, policies.TotalPages);
    }

    [Fact]
    public async Task PolicyListCountsOnlyTheLatestAssignmentForEachEnrollment()
    {
        var enrollmentId = await AddDeviceAsync("reassigned-device");
        var previousPolicyId = Guid.NewGuid();
        var currentPolicyId = Guid.NewGuid();
        _db.Policies.AddRange(
            new Policy
            {
                PolicyId = previousPolicyId,
                Creator = "admin",
                PolicyJson = """{"name":"Previous","type":"Security"}"""
            },
            new Policy
            {
                PolicyId = currentPolicyId,
                Creator = "admin",
                PolicyJson = """{"name":"Current","type":"Security"}"""
            });
        _db.PolicyAssignments.AddRange(
            new PolicyAssignment
            {
                AssignmentId = Guid.NewGuid(),
                EnrollmentId = enrollmentId,
                PolicyId = previousPolicyId,
                AssignedVersion = 1,
                AcknowledgedVersion = 1,
                AssignedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            },
            new PolicyAssignment
            {
                AssignmentId = Guid.NewGuid(),
                EnrollmentId = enrollmentId,
                PolicyId = currentPolicyId,
                AssignedVersion = 1,
                AssignedAt = DateTimeOffset.UtcNow
            });
        await _db.SaveChangesAsync();

        var action = await _controller.List();
        var response = Assert.IsType<OkObjectResult>(action.Result);
        var policies = Assert.IsType<ManagedPoliciesResponse>(response.Value);

        var previous = Assert.Single(policies.Items, policy => policy.Name == "Previous");
        var current = Assert.Single(policies.Items, policy => policy.Name == "Current");
        Assert.Equal(0, previous.DeviceCount);
        Assert.Equal(1, current.DeviceCount);
        Assert.Equal(0, current.CompliantDeviceCount);
    }

    private async Task<Guid> AddDeviceAsync(
        string model,
        DeviceState state = DeviceState.Active,
        bool accessDisabled = false)
    {
        var enrollmentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var credential = new BootstrapTokenCredential(
            enrollmentId,
            new string('a', 64),
            now,
            now.AddMinutes(5));
        var enrollment = PendingEnrollment.Create(credential);
        var device = Device.Create(Guid.NewGuid(), enrollmentId, state);
        device.UpdateProfile("phone", "Test", model, 14);
        if (accessDisabled)
            device.SetAccessDisabled(true);

        _db.PendingEnrollments.Add(enrollment);
        _db.Devices.Add(device);
        await _db.SaveChangesAsync();
        return enrollmentId;
    }
}
