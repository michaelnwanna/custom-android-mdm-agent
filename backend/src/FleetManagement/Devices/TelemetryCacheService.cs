namespace FleetManagement.Devices;

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

/// <summary>
/// In-memory cache implementation for telemetry metrics.
/// Simple thread-safe cache using ConcurrentDictionary.
/// (In production, would use Redis or other distributed cache)
/// </summary>
public class TelemetryCacheService
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly TelemetryCacheSettings _settings;

    public TelemetryCacheService(TelemetryCacheSettings? settings = null)
    {
        _settings = settings ?? new TelemetryCacheSettings();
    }

    /// <summary>
    /// Set a value in the cache with TTL.
    /// </summary>
    public void Set<T>(string key, T value, int ttlSeconds)
    {
        if (!_settings.EnableCaching)
            return;

        var entry = new CacheEntry
        {
            Value = value,
            ExpiryTime = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _cache[key] = entry;
    }

    /// <summary>
    /// Get a value from cache if not expired.
    /// </summary>
    public bool TryGetValue<T>(string key, out T? value) where T : class
    {
        value = null;

        if (!_settings.EnableCaching || !_cache.TryGetValue(key, out var entry))
            return false;

        if (DateTimeOffset.UtcNow >= entry.ExpiryTime)
        {
            _cache.TryRemove(key, out _);
            return false;
        }

        value = entry.Value as T;
        return value != null;
    }

    /// <summary>
    /// Remove a key from cache (for invalidation).
    /// </summary>
    public void Remove(string key)
    {
        _cache.TryRemove(key, out _);
    }

    /// <summary>
    /// Clear all cache entries.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Get cache statistics.
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        // Remove expired entries
        var now = DateTimeOffset.UtcNow;
        var expired = _cache.Where(kvp => kvp.Value.ExpiryTime <= now).Select(kvp => kvp.Key).ToList();
        foreach (var key in expired)
            _cache.TryRemove(key, out _);

        return new CacheStatistics
        {
            TotalEntries = _cache.Count,
            IsEnabled = _settings.EnableCaching,
            CheckedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Cache entry with expiry tracking.
    /// </summary>
    private class CacheEntry
    {
        public object? Value { get; set; }
        public DateTimeOffset ExpiryTime { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}

/// <summary>
/// Cache statistics for monitoring.
/// </summary>
public class CacheStatistics
{
    public int TotalEntries { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
}

/// <summary>
/// Extension methods for common cache key patterns.
/// </summary>
public static class TelometryCacheKeys
{
    private const string FleetHealthMetricsKey = "fleet:health:metrics";
    private const string DeviceLatestTelemetryKeyPrefix = "device:telemetry:latest:";
    private const string DeviceHealthAggregateKeyPrefix = "device:health:agg:";

    public static string FleetHealthMetrics() => FleetHealthMetricsKey;

    public static string DeviceLatestTelemetry(Guid deviceId) => $"{DeviceLatestTelemetryKeyPrefix}{deviceId}";

    public static string DeviceHealthAggregate(Guid deviceId, int hoursBack) => 
        $"{DeviceHealthAggregateKeyPrefix}{deviceId}:{hoursBack}h";

    /// <summary>
    /// Invalidate all cache entries related to a device.
    /// Called when device telemetry is updated.
    /// </summary>
    public static List<string> InvalidateDeviceKeys(Guid deviceId)
    {
        return new List<string>
        {
            FleetHealthMetrics(),
            DeviceLatestTelemetry(deviceId),
            // Note: Health aggregate cache for all time ranges should be cleared
            // In production, would use a pattern like invalidatePattern
        };
    }
}
