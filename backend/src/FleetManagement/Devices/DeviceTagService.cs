namespace FleetManagement.Devices;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Service for managing device tags and assignments.
/// Handles tag CRUD operations, device tagging, and bulk queries.
/// </summary>
public sealed class DeviceTagService
{
    private readonly FleetDbContext _db;

    public DeviceTagService(FleetDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>
    /// Create a new device tag.
    /// </summary>
    /// <exception cref="InvalidOperationException">Tag name already exists</exception>
    public async Task<DeviceTag> CreateTagAsync(string name, string createdBy, string? description = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tag name cannot be empty", nameof(name));
        if (string.IsNullOrWhiteSpace(createdBy))
            throw new ArgumentException("CreatedBy cannot be empty", nameof(createdBy));

        var normalizedName = name.Trim().ToLowerInvariant();

        // Check for existing tag
        var existing = await _db.DeviceTags
            .Where(t => t.Name == normalizedName)
            .FirstOrDefaultAsync(ct);

        if (existing != null)
            throw new InvalidOperationException($"Tag '{name}' already exists");

        var tag = DeviceTag.Create(name, createdBy, description);
        _db.DeviceTags.Add(tag);
        await _db.SaveChangesAsync(ct);

        return tag;
    }

    /// <summary>
    /// Get a tag by ID.
    /// </summary>
    public async Task<DeviceTag?> GetTagAsync(Guid tagId, CancellationToken ct = default)
    {
        return await _db.DeviceTags
            .Where(t => t.TagId == tagId)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Get a tag by normalized name.
    /// </summary>
    public async Task<DeviceTag?> GetTagByNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var normalizedName = name.Trim().ToLowerInvariant();
        return await _db.DeviceTags
            .Where(t => t.Name == normalizedName)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Get all tags (optionally filtered by active status).
    /// </summary>
    public async Task<List<DeviceTag>> GetTagsAsync(bool? onlyActive = null, CancellationToken ct = default)
    {
        var query = _db.DeviceTags.AsQueryable();

        if (onlyActive.HasValue)
            query = query.Where(t => t.IsActive == onlyActive.Value);

        return await query
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Update tag description and active status.
    /// </summary>
    public async Task UpdateTagAsync(Guid tagId, string? description, bool isActive, CancellationToken ct = default)
    {
        var tag = await _db.DeviceTags
            .Where(t => t.TagId == tagId)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Tag {tagId} not found");

        tag.Update(description, isActive);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Assign a tag to a device.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Device or tag not found</exception>
    /// <exception cref="InvalidOperationException">Tag already assigned to device</exception>
    public async Task AssignTagAsync(Guid deviceId, Guid tagId, string assignedBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(assignedBy))
            throw new ArgumentException("AssignedBy cannot be empty", nameof(assignedBy));

        // Verify device exists
        var device = await _db.Devices
            .Where(d => d.DeviceId == deviceId)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Device {deviceId} not found");

        // Verify tag exists
        var tag = await _db.DeviceTags
            .Where(t => t.TagId == tagId)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Tag {tagId} not found");

        // Check if tag is already assigned (active)
        var existing = await _db.DeviceTagAssignments
            .Where(a => a.DeviceId == deviceId && a.TagId == tagId && a.RemovedAt == null)
            .FirstOrDefaultAsync(ct);

        if (existing != null)
            throw new InvalidOperationException($"Tag {tagId} is already assigned to device {deviceId}");

        var assignment = DeviceTagAssignment.Create(deviceId, tagId, assignedBy);
        _db.DeviceTagAssignments.Add(assignment);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Remove a tag from a device (soft-delete).
    /// </summary>
    /// <exception cref="KeyNotFoundException">Assignment not found</exception>
    /// <exception cref="InvalidOperationException">Assignment already removed</exception>
    public async Task RemoveTagAsync(Guid deviceId, Guid tagId, string removedBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(removedBy))
            throw new ArgumentException("RemovedBy cannot be empty", nameof(removedBy));

        var assignment = await _db.DeviceTagAssignments
            .Where(a => a.DeviceId == deviceId && a.TagId == tagId && a.RemovedAt == null)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"No active assignment found for device {deviceId} and tag {tagId}");

        assignment.Remove(removedBy);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Get all tags assigned to a device (active assignments only).
    /// </summary>
    public async Task<List<DeviceTag>> GetDeviceTagsAsync(Guid deviceId, CancellationToken ct = default)
    {
        return await _db.DeviceTagAssignments
            .Where(a => a.DeviceId == deviceId && a.RemovedAt == null)
            .Select(a => a.Tag)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get all devices with a specific tag (active assignments only).
    /// </summary>
    public async Task<List<Device>> GetDevicesWithTagAsync(Guid tagId, CancellationToken ct = default)
    {
        return await _db.DeviceTagAssignments
            .Where(a => a.TagId == tagId && a.RemovedAt == null)
            .Select(a => a.Device)
            .OrderBy(d => d.DeviceId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get all devices with a specific tag name (normalized).
    /// </summary>
    public async Task<List<Device>> GetDevicesWithTagByNameAsync(string tagName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            return new();

        var normalizedName = tagName.Trim().ToLowerInvariant();
        return await _db.DeviceTagAssignments
            .Where(a => a.Tag.Name == normalizedName && a.RemovedAt == null)
            .Select(a => a.Device)
            .OrderBy(d => d.DeviceId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get all devices with any of the specified tags (active assignments only).
    /// Uses OR logic: device must have at least one of the tags.
    /// </summary>
    public async Task<List<Device>> GetDevicesWithAnyTagAsync(IEnumerable<Guid> tagIds, CancellationToken ct = default)
    {
        var tagIdList = tagIds.ToList();
        if (!tagIdList.Any())
            return new();

        return await _db.DeviceTagAssignments
            .Where(a => tagIdList.Contains(a.TagId) && a.RemovedAt == null)
            .Select(a => a.Device)
            .Distinct()
            .OrderBy(d => d.DeviceId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get all devices with all of the specified tags (active assignments only).
    /// Uses AND logic: device must have all tags.
    /// </summary>
    public async Task<List<Device>> GetDevicesWithAllTagsAsync(IEnumerable<Guid> tagIds, CancellationToken ct = default)
    {
        var tagIdList = tagIds.ToList();
        if (!tagIdList.Any())
            return new();

        var requiredTagCount = tagIdList.Count;

        // Get all active assignments for the specified tags
        var assignments = await _db.DeviceTagAssignments
            .Where(a => tagIdList.Contains(a.TagId) && a.RemovedAt == null)
            .ToListAsync(ct);

        // Group by device and filter to those with all required tags
        var deviceIds = assignments
            .GroupBy(a => a.DeviceId)
            .Where(g => g.Count() == requiredTagCount)
            .Select(g => g.Key)
            .ToList();

        if (!deviceIds.Any())
            return new();

        return await _db.Devices
            .Where(d => deviceIds.Contains(d.DeviceId))
            .OrderBy(d => d.DeviceId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Get assignment history for a device and tag (including removed assignments).
    /// </summary>
    public async Task<List<DeviceTagAssignment>> GetAssignmentHistoryAsync(Guid deviceId, Guid tagId, CancellationToken ct = default)
    {
        return await _db.DeviceTagAssignments
            .Where(a => a.DeviceId == deviceId && a.TagId == tagId)
            .OrderByDescending(a => a.AssignedAt)
            .ToListAsync(ct);
    }
}
