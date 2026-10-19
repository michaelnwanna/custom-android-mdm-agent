namespace FleetManagement.Devices;

using System;

/// <summary>
/// Configuration for telemetry caching behavior.
/// Controls cache TTL and invalidation strategies.
/// </summary>
public class TelemetryCacheSettings
{
    /// <summary>
    /// Cache TTL for fleet health metrics in seconds (default: 300 = 5 minutes).
    /// </summary>
    public int FleetHealthCacheTtlSeconds { get; set; } = 300;

    /// <summary>
    /// Cache TTL for per-device latest telemetry in seconds (default: 120 = 2 minutes).
    /// </summary>
    public int DeviceLatestTelemetryCacheTtlSeconds { get; set; } = 120;

    /// <summary>
    /// Cache TTL for device health aggregates in seconds (default: 600 = 10 minutes).
    /// </summary>
    public int DeviceAggregationCacheTtlSeconds { get; set; } = 600;

    /// <summary>
    /// Whether caching is enabled (default: true).
    /// Can be disabled for testing or debugging.
    /// </summary>
    public bool EnableCaching { get; set; } = true;
}

/// <summary>
/// Comprehensive fleet health metrics for dashboard consumption.
/// Aggregates all key health indicators into a single object.
/// </summary>
public class FleetHealthMetrics
{
    /// <summary>
    /// Snapshot timestamp of when metrics were computed.
    /// </summary>
    public DateTimeOffset ComputedAt { get; set; }

    /// <summary>
    /// Total number of devices in fleet.
    /// </summary>
    public int TotalDevices { get; set; }

    /// <summary>
    /// Number of devices currently online (connected).
    /// </summary>
    public int OnlineDevices { get; set; }

    /// <summary>
    /// Number of devices currently offline (no connectivity).
    /// </summary>
    public int OfflineDevices { get; set; }

    /// <summary>
    /// Percentage of fleet that is online.
    /// </summary>
    public double OnlinePercentage { get; set; }

    /// <summary>
    /// Average battery level across all devices.
    /// </summary>
    public int AverageBatteryLevel { get; set; }

    /// <summary>
    /// Number of devices with battery below 20% threshold.
    /// </summary>
    public int LowBatteryDevices { get; set; }

    /// <summary>
    /// Percentage of devices with low battery.
    /// </summary>
    public double LowBatteryPercentage { get; set; }

    /// <summary>
    /// Number of devices with battery below 5% (critical).
    /// </summary>
    public int CriticalBatteryDevices { get; set; }

    /// <summary>
    /// Average storage usage percentage across fleet.
    /// </summary>
    public int AverageStorageUsedPercent { get; set; }

    /// <summary>
    /// Number of devices with storage below 10% free.
    /// </summary>
    public int LowStorageDevices { get; set; }

    /// <summary>
    /// Average memory usage percentage across fleet.
    /// </summary>
    public int AverageMemoryUsedPercent { get; set; }

    /// <summary>
    /// Average CPU usage percentage across fleet.
    /// </summary>
    public int AverageCpuUsagePercent { get; set; }

    /// <summary>
    /// Connectivity breakdown across fleet.
    /// </summary>
    public ConnectivityBreakdownMetrics Connectivity { get; set; } = new();

    /// <summary>
    /// Health alert statistics.
    /// </summary>
    public HealthAlertMetrics Alerts { get; set; } = new();

    /// <summary>
    /// Cache info for debugging/monitoring.
    /// </summary>
    public CacheInfo? CacheInfo { get; set; }
}

/// <summary>
/// Connectivity metrics for fleet.
/// </summary>
public class ConnectivityBreakdownMetrics
{
    public int WiFiDevices { get; set; }
    public int CellularDevices { get; set; }
    public int OfflineDevices { get; set; }
    public int AirplaneModeDevices { get; set; }
    public int UnknownDevices { get; set; }

    public double WiFiPercentage { get; set; }
    public double CellularPercentage { get; set; }
    public double OfflinePercentage { get; set; }
    public double AirplaneModePercentage { get; set; }
    public double UnknownPercentage { get; set; }

    /// <summary>
    /// Determine dominant connectivity mode.
    /// </summary>
    public ConnectivityStatus DominantMode
    {
        get
        {
            return new (ConnectivityStatus, int)[]
            {
                (ConnectivityStatus.WiFi, WiFiDevices),
                (ConnectivityStatus.Cellular, CellularDevices),
                (ConnectivityStatus.Offline, OfflineDevices),
                (ConnectivityStatus.AirplaneMode, AirplaneModeDevices),
                (ConnectivityStatus.Unknown, UnknownDevices)
            }
            .OrderByDescending(x => x.Item2)
            .First()
            .Item1;
        }
    }
}

/// <summary>
/// Health alert metrics for fleet.
/// </summary>
public class HealthAlertMetrics
{
    public int CriticalAlerts { get; set; }
    public int WarningAlerts { get; set; }
    public int InfoAlerts { get; set; }
    public int TotalActiveAlerts { get; set; }

    public int DevicesWithCriticalAlerts { get; set; }
    public int DevicesWithAnyAlert { get; set; }

    public double CriticalAlertsPercentage { get; set; }
    public double WarningAlertsPercentage { get; set; }
    public double InfoAlertsPercentage { get; set; }

    /// <summary>
    /// Alert health score (0-100). 100 = no alerts, 0 = critical alerts on all devices.
    /// </summary>
    public int HealthScore { get; set; }
}

/// <summary>
/// Information about when metrics came from cache vs fresh computation.
/// </summary>
public class CacheInfo
{
    public bool FromCache { get; set; }
    public DateTimeOffset? CacheExpiry { get; set; }
    public TimeSpan? TimeToLiveRemaining { get; set; }
    public int? CacheHitCount { get; set; }
}
