namespace FleetManagement.Devices;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Service for managing device lifecycle and metadata.
/// Handles device creation from enrollments, profile updates, and device queries.
/// </summary>
public sealed class DeviceService
{
    private readonly FleetDbContext _db;

    public DeviceService(FleetDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>
    /// Create a new device from a claimed enrollment.
    /// Called during enrollment completion when DeviceId is first assigned.
    /// </summary>
    /// <exception cref="InvalidOperationException">Enrollment not found or already has device</exception>
    public async Task<Device> CreateDeviceFromEnrollmentAsync(Guid enrollmentId, CancellationToken ct = default)
    {
        var enrollment = await _db.PendingEnrollments
            .Where(e => e.Id == enrollmentId)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Enrollment {enrollmentId} not found");

        if (!enrollment.DeviceId.HasValue || enrollment.DeviceId == Guid.Empty)
            throw new InvalidOperationException($"Enrollment {enrollmentId} has not been claimed yet");

        // Check if device already exists
        var existing = await _db.Devices
            .Where(d => d.EnrollmentId == enrollmentId)
            .FirstOrDefaultAsync(ct);

        if (existing != null)
            throw new InvalidOperationException($"Device already exists for enrollment {enrollmentId}");

        var device = Device.Create(enrollment.DeviceId.Value, enrollmentId);
        _db.Devices.Add(device);
        await _db.SaveChangesAsync(ct);

        return device;
    }

    /// <summary>
    /// Get a device by ID.
    /// </summary>
    public async Task<Device?> GetDeviceAsync(Guid deviceId, CancellationToken ct = default)
    {
        return await _db.Devices
            .Where(d => d.DeviceId == deviceId)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Get a device by enrollment ID.
    /// </summary>
    public async Task<Device?> GetDeviceByEnrollmentAsync(Guid enrollmentId, CancellationToken ct = default)
    {
        return await _db.Devices
            .Where(d => d.EnrollmentId == enrollmentId)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Get all active devices.
    /// </summary>
    public async Task<List<Device>> GetActiveDevicesAsync(CancellationToken ct = default)
    {
        return await _db.Devices
            .Where(d => d.State == DeviceState.Active)
            .OrderBy(d => d.DeviceId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get all devices in a specific state.
    /// </summary>
    public async Task<List<Device>> GetDevicesByStateAsync(DeviceState state, CancellationToken ct = default)
    {
        return await _db.Devices
            .Where(d => d.State == state)
            .OrderBy(d => d.DeviceId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get devices that haven't been seen since a specific time.
    /// Useful for detecting offline or lost devices.
    /// </summary>
    public async Task<List<Device>> GetInactiveDevicesAsync(TimeSpan inactiveDuration, CancellationToken ct = default)
    {
        var threshold = DateTimeOffset.UtcNow - inactiveDuration;
        return await _db.Devices
            .Where(d => d.LastSeenAt < threshold)
            .OrderBy(d => d.LastSeenAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Update device metadata from device profile.
    /// Called from device profile updates or status reports.
    /// </summary>
    public async Task UpdateDeviceProfileAsync(
        Guid deviceId,
        string? deviceType,
        string? manufacturer,
        string? model,
        int? osVersion,
        CancellationToken ct = default)
    {
        var device = await _db.Devices
            .Where(d => d.DeviceId == deviceId)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Device {deviceId} not found");

        device.UpdateProfile(deviceType, manufacturer, model, osVersion);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Update the last-seen timestamp on device activity.
    /// Called from MQTT status reports, acknowledgements, or API calls.
    /// </summary>
    public async Task UpdateLastSeenAsync(Guid deviceId, CancellationToken ct = default)
    {
        var device = await _db.Devices
            .Where(d => d.DeviceId == deviceId)
            .FirstOrDefaultAsync(ct);

        if (device != null)
        {
            device.UpdateLastSeen();
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Set or clear the access-disabled flag for a device.
    /// </summary>
    public async Task SetAccessDisabledAsync(Guid deviceId, bool disabled, CancellationToken ct = default)
    {
        var device = await _db.Devices
            .Where(d => d.DeviceId == deviceId)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Device {deviceId} not found");

        device.SetAccessDisabled(disabled);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Transition a device to a new state.
    /// </summary>
    public async Task TransitionDeviceStateAsync(Guid deviceId, DeviceState newState, CancellationToken ct = default)
    {
        var device = await _db.Devices
            .Where(d => d.DeviceId == deviceId)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Device {deviceId} not found");

        device.TransitionToState(newState);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Get all devices, optionally filtered by state.
    /// </summary>
    public async Task<List<Device>> GetAllDevicesAsync(DeviceState? state = null, CancellationToken ct = default)
    {
        var query = _db.Devices.AsQueryable();

        if (state.HasValue)
            query = query.Where(d => d.State == state);

        return await query
            .OrderBy(d => d.DeviceId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get count of devices by state.
    /// Useful for analytics dashboards.
    /// </summary>
    public async Task<Dictionary<DeviceState, int>> GetDeviceCountByStateAsync(CancellationToken ct = default)
    {
        var counts = await _db.Devices
            .GroupBy(d => d.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return counts.ToDictionary(x => x.State, x => x.Count);
    }

    /// <summary>
    /// Get devices with access disabled.
    /// Useful for auditing or compliance checks.
    /// </summary>
    public async Task<List<Device>> GetAccessDisabledDevicesAsync(CancellationToken ct = default)
    {
        return await _db.Devices
            .Where(d => d.AccessDisabled)
            .OrderBy(d => d.DeviceId)
            .ToListAsync(ct);
    }
}
