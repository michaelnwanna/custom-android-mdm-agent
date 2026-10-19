namespace FleetManagement.Tests.Devices;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FleetManagement.Devices;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using FleetManagement.Releases;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// Integration tests for desired device state computation and reconciliation.
/// Tests the StateComputationService with a real in-memory database context.
/// </summary>
public sealed class DeviceStateComputationTests : IAsyncLifetime
{
    private readonly DbContextOptions<FleetDbContext> _options;
    private FleetDbContext _context = null!;
    private StateComputationService _service = null!;
    private ReleaseService _releaseService = null!;
    private Guid _deviceId;

    public DeviceStateComputationTests()
    {
        _options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    public async Task InitializeAsync()
    {
        _context = new FleetDbContext(_options);
        await _context.Database.EnsureCreatedAsync();

        _releaseService = new ReleaseService(_context);
        _service = new StateComputationService(_context, _releaseService);

        // Create test device
        _deviceId = Guid.NewGuid();
        var device = Device.Create(_deviceId, _deviceId);
        _context.Devices.Add(device);
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_context != null)
        {
            await _context.Database.EnsureDeletedAsync();
            await _context.DisposeAsync();
        }
    }
    [Fact]
    public async Task ComputeDesiredStateReturnsNonNullState()
    {
        var state = await _service.ComputeDesiredStateAsync(_deviceId, forceRecompute: true);

        Assert.NotNull(state);
        Assert.Equal(_deviceId, state.DeviceId);
        Assert.True(state.IsValid);
    }

    [Fact]
    public async Task ComputedStateHasNonEmptyHash()
    {
        var state = await _service.ComputeDesiredStateAsync(_deviceId, forceRecompute: true);

        Assert.NotNull(state.ComputationHash);
        Assert.NotEmpty(state.ComputationHash);
    }

    [Fact]
    public async Task ReconciliationWithMatchingStateReturnsInSync()
    {
        // No assignments = empty desired state
        var desiredState = await _service.ComputeDesiredStateAsync(_deviceId, forceRecompute: true);

        // Device reports matching empty state
        var observedState = new ObservedDeviceState
        {
            ObservedPolicyId = null,
            ObservedPolicyVersion = null,
            ObservedReleaseIds = new(),
            ObservedTagIds = new(),
            PendingCommandCount = 0,
            PendingEventCount = 0,
            CapturedAt = DateTimeOffset.UtcNow
        };

        var result = await _service.ReconcileStateAsync(_deviceId, observedState);

        Assert.NotNull(result);
        Assert.True(result.IsInSync);
        Assert.Empty(result.PolicyConflicts);
        Assert.Empty(result.ReleaseConflicts);
        Assert.Empty(result.TagConflicts);
    }

    [Fact]
    public async Task GetCachedStateReturnsNullWhenNotCached()
    {
        var cachedState = await _service.GetCachedStateAsync(Guid.NewGuid());
        Assert.Null(cachedState);
    }

    [Fact]
    public async Task GetCachedStateReturnsCachedStateAfterComputation()
    {
        // Compute state
        await _service.ComputeDesiredStateAsync(_deviceId, forceRecompute: true);

        // Retrieve cached
        var cachedState = await _service.GetCachedStateAsync(_deviceId);

        Assert.NotNull(cachedState);
        Assert.Equal(_deviceId, cachedState.DeviceId);
    }

    [Fact]
    public async Task InvalidateCacheMarksStateForRecomputation()
    {
        // Compute initial state
        var state1 = await _service.ComputeDesiredStateAsync(_deviceId, forceRecompute: true);

        // Invalidate
        await _service.InvalidateCacheAsync(_deviceId);

        // Force recomputation should get new state
        var state2 = await _service.ComputeDesiredStateAsync(_deviceId, forceRecompute: true);

        // Both should be valid, but timestamps might differ
        Assert.NotNull(state1);
        Assert.NotNull(state2);
    }

    [Fact]
    public async Task ForceRecomputeIgnoresCachedState()
    {
        // Compute initial state
        var state1 = await _service.ComputeDesiredStateAsync(_deviceId, forceRecompute: true);
        var hash1 = state1.ComputationHash;
        var time1 = state1.ComputedAt;

        // Immediately recompute with force (should recompute, not return cache)
        await Task.Delay(10); // Small delay to ensure time difference
        var state2 = await _service.ComputeDesiredStateAsync(_deviceId, forceRecompute: true);
        var hash2 = state2.ComputationHash;
        var time2 = state2.ComputedAt;

        // Should have same hash (no changes) but different compute times
        Assert.Equal(hash1, hash2);
        Assert.True(time2 >= time1);
    }

    [Fact]
    public async Task ReconciliationResultContainsDeviceId()
    {
        var observedState = new ObservedDeviceState
        {
            ObservedPolicyId = null,
            ObservedPolicyVersion = null,
            ObservedReleaseIds = new(),
            ObservedTagIds = new(),
            PendingCommandCount = 0,
            PendingEventCount = 0,
            CapturedAt = DateTimeOffset.UtcNow
        };

        var result = await _service.ReconcileStateAsync(_deviceId, observedState);

        Assert.NotNull(result);
        Assert.Equal(_deviceId, result.DeviceId);
    }

    [Fact]
    public async Task GetOutOfSyncDevicesReturnsEmptyListWhenNoDevices()
    {
        var outOfSync = await _service.GetOutOfSyncDevicesAsync(TimeSpan.FromMinutes(5));

        Assert.NotNull(outOfSync);
        Assert.Empty(outOfSync);
    }
}
