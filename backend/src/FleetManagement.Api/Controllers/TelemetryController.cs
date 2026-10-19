namespace FleetManagement.Api.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FleetManagement.Devices;
using FleetManagement.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// REST API for device telemetry and health monitoring.
/// Endpoints enable devices to submit telemetry and clients to query health data.
/// </summary>
[ApiController]
[Route("api/telemetry")]
public class TelemetryController : ControllerBase
{
    private readonly FleetDbContext _context;
    private readonly TelemetryCollectionService _telemetryService;
    private readonly TelemetryCacheService _cacheService;

    public TelemetryController(
        FleetDbContext context, 
        TelemetryCollectionService telemetryService,
        TelemetryCacheService cacheService)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
    }

    /// <summary>
    /// Device submits current telemetry to server.
    /// POST /api/telemetry/report
    /// </summary>
    [HttpPost("report")]
    public async Task<ActionResult<RecordTelemetryResponse>> SubmitTelemetryAsync([FromBody] SubmitTelemetryRequest request)
    {
        if (request == null)
            return BadRequest("Request cannot be null");

        try
        {
            var result = await _telemetryService.RecordTelemetryAsync(
                request.DeviceId,
                request.BatteryLevel,
                request.IsCharging,
                request.StorageUsedMb,
                request.StorageTotalMb,
                request.AvailableMemoryMb,
                request.TotalMemoryMb,
                request.Connectivity,
                request.WifiSignalStrengthDbm,
                request.CellularSignalStrengthDbm,
                request.CpuUsagePercent,
                request.SnapshotTakenAt);

            // Auto-resolve alerts if conditions improved
            await _telemetryService.AutoResolveAlertsAsync(request.DeviceId, 
                await _telemetryService.GetLatestTelemetryAsync(request.DeviceId) 
                ?? throw new InvalidOperationException("Telemetry not found after recording"));

            return Ok(RecordTelemetryResponse.FromResult(result));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get latest telemetry snapshot for a device.
    /// GET /api/devices/{deviceId}/telemetry/latest
    /// </summary>
    [HttpGet("/api/devices/{deviceId}/telemetry/latest")]
    public async Task<ActionResult<TelemetrySnapshot>> GetLatestTelemetryAsync(Guid deviceId)
    {
        var telemetry = await _telemetryService.GetLatestTelemetryAsync(deviceId);
        if (telemetry == null)
            return NotFound();

        return Ok(TelemetrySnapshot.FromDomain(telemetry));
    }

    /// <summary>
    /// Get historical telemetry for a device with pagination and date filtering.
    /// GET /api/devices/{deviceId}/telemetry/history?page=1&pageSize=50&daysBack=7
    /// </summary>
    [HttpGet("/api/devices/{deviceId}/telemetry/history")]
    public async Task<ActionResult<PaginatedResult<TelemetrySnapshot>>> GetTelemetryHistoryAsync(
        Guid deviceId,
        int page = 1,
        int pageSize = 50,
        int? daysBack = null)
    {
        if (page < 1 || pageSize < 1 || pageSize > 500)
            return BadRequest("Page must be ≥1, pageSize must be 1-500");

        daysBack ??= 30;

        var cutoffDate = DateTimeOffset.UtcNow.AddDays(-daysBack.Value);
        var query = _context.DeviceTelemetries
            .Where(t => t.DeviceId == deviceId && t.CreatedAt >= cutoffDate)
            .OrderByDescending(t => t.CreatedAt);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => TelemetrySnapshot.FromDomain(t))
            .ToListAsync();

        return Ok(new PaginatedResult<TelemetrySnapshot>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (totalCount + pageSize - 1) / pageSize
        });
    }

    /// <summary>
    /// Get health alerts for a device (active and resolved).
    /// GET /api/devices/{deviceId}/health/alerts?activeOnly=true&page=1
    /// </summary>
    [HttpGet("/api/devices/{deviceId}/health/alerts")]
    public async Task<ActionResult<PaginatedResult<HealthAlertDto>>> GetDeviceAlertsAsync(
        Guid deviceId,
        bool activeOnly = true,
        int page = 1,
        int pageSize = 50)
    {
        if (page < 1 || pageSize < 1 || pageSize > 500)
            return BadRequest("Page must be ≥1, pageSize must be 1-500");

        var query = _context.DeviceHealthAlerts
            .Where(a => a.DeviceId == deviceId);

        if (activeOnly)
            query = query.Where(a => a.ResolvedAt == null);

        query = query.OrderByDescending(a => a.CreatedAt);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => HealthAlertDto.FromDomain(a))
            .ToListAsync();

        return Ok(new PaginatedResult<HealthAlertDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (totalCount + pageSize - 1) / pageSize
        });
    }

    /// <summary>
    /// Get aggregated health metrics for a device over a time period.
    /// GET /api/devices/{deviceId}/health/aggregate?hoursBack=24
    /// </summary>
    [HttpGet("/api/devices/{deviceId}/health/aggregate")]
    public async Task<ActionResult<AggregatedHealthDto>> GetAggregatedHealthAsync(
        Guid deviceId,
        int hoursBack = 24)
    {
        if (hoursBack < 1 || hoursBack > 8760) // Max 1 year
            return BadRequest("hoursBack must be between 1 and 8760");

        var health = await _telemetryService.AggregateDeviceHealthAsync(deviceId, hoursBack);
        if (health == null)
            return NotFound("No telemetry data for this device");

        return Ok(AggregatedHealthDto.FromDomain(health));
    }

    /// <summary>
    /// Get fleet-wide health summary.
    /// GET /api/fleet/health/summary
    /// </summary>
    [HttpGet("/api/fleet/health/summary")]
    public async Task<ActionResult<FleetHealthSummary>> GetFleetHealthSummaryAsync()
    {
        var totalDevices = await _context.Devices.CountAsync();

        // Get latest telemetry for all devices
        var latestTelemetries = await _context.DeviceTelemetries
            .GroupBy(t => t.DeviceId)
            .Select(g => g.OrderByDescending(t => t.CreatedAt).First())
            .ToListAsync();

        if (latestTelemetries.Count == 0)
            return Ok(new FleetHealthSummary
            {
                TotalDevices = totalDevices,
                OnlineDevices = 0,
                OfflineDevices = 0
            });

        var onlineDevices = latestTelemetries.Count(t => t.Connectivity != ConnectivityStatus.Offline);
        var offlineDevices = totalDevices - onlineDevices;

        var avgBattery = (int)latestTelemetries.Average(t => t.BatteryLevel);
        var lowBatteryDevices = latestTelemetries.Count(t => t.BatteryLevel <= 20);

        var avgStorageUsed = latestTelemetries.Average(t => 
            t.StorageTotalMb > 0 ? (t.StorageUsedMb * 100.0) / t.StorageTotalMb : 0);
        var lowStorageDevices = latestTelemetries.Count(t => 
            t.StorageTotalMb > 0 && (t.StorageFreeMb * 100.0) / t.StorageTotalMb <= 10);

        var connectivity = new ConnectivityBreakdown
        {
            WiFi = latestTelemetries.Count(t => t.Connectivity == ConnectivityStatus.WiFi),
            Cellular = latestTelemetries.Count(t => t.Connectivity == ConnectivityStatus.Cellular),
            Offline = latestTelemetries.Count(t => t.Connectivity == ConnectivityStatus.Offline),
            AirplaneMode = latestTelemetries.Count(t => t.Connectivity == ConnectivityStatus.AirplaneMode),
            Unknown = latestTelemetries.Count(t => t.Connectivity == ConnectivityStatus.Unknown)
        };

        // Get alert statistics
        var alertStats = await GetAlertStatisticsAsync();

        return Ok(new FleetHealthSummary
        {
            TotalDevices = totalDevices,
            OnlineDevices = onlineDevices,
            OfflineDevices = offlineDevices,
            AverageBatteryLevel = avgBattery,
            LowBatteryDevices = lowBatteryDevices,
            AverageStorageUsedPercent = (int)avgStorageUsed,
            LowStorageDevices = lowStorageDevices,
            Connectivity = connectivity,
            AlertStats = alertStats,
            LastUpdated = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Get devices with critical health alerts.
    /// GET /api/fleet/health/at-risk?limit=100
    /// </summary>
    [HttpGet("/api/fleet/health/at-risk")]
    public async Task<ActionResult<List<AtRiskDevice>>> GetAtRiskDevicesAsync(int limit = 100)
    {
        if (limit < 1 || limit > 1000)
            return BadRequest("Limit must be between 1 and 1000");

        // Get all active critical alerts, grouped by device
        var criticalAlerts = await _context.DeviceHealthAlerts
            .Where(a => a.Severity == AlertSeverity.Critical && a.ResolvedAt == null)
            .GroupBy(a => a.DeviceId)
            .Select(g => new { DeviceId = g.Key, AlertCount = g.Count() })
            .OrderByDescending(a => a.AlertCount)
            .Take(limit)
            .ToListAsync();

        var atRiskDevices = new List<AtRiskDevice>();

        foreach (var item in criticalAlerts)
        {
            var device = await _context.Devices.FirstOrDefaultAsync(d => d.DeviceId == item.DeviceId);
            var alerts = await _context.DeviceHealthAlerts
                .Where(a => a.DeviceId == item.DeviceId && a.Severity == AlertSeverity.Critical && a.ResolvedAt == null)
                .Select(a => HealthAlertDto.FromDomain(a))
                .ToListAsync();

            var latestTelemetry = await _telemetryService.GetLatestTelemetryAsync(item.DeviceId);

            atRiskDevices.Add(new AtRiskDevice
            {
                DeviceId = item.DeviceId,
                DeviceName = device?.Model ?? "Unknown",
                CriticalAlertCount = item.AlertCount,
                Alerts = alerts,
                LatestBatteryLevel = latestTelemetry?.BatteryLevel ?? 0,
                LatestConnectivity = latestTelemetry?.Connectivity ?? ConnectivityStatus.Unknown,
                LastUpdated = latestTelemetry?.CreatedAt ?? DateTimeOffset.UnixEpoch
            });
        }

        return Ok(atRiskDevices);
    }

    /// <summary>
    /// Manually resolve a health alert.
    /// POST /api/health/alerts/{alertId}/resolve
    /// </summary>
    [HttpPost("/api/health/alerts/{alertId}/resolve")]
    public async Task<ActionResult> ResolveAlertAsync(Guid alertId, [FromBody] ResolveAlertRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest("Reason must be provided");

        var success = await _telemetryService.ResolveAlertAsync(alertId, request.Reason);
        if (!success)
            return NotFound("Alert not found or already resolved");

        return Ok();
    }

    /// <summary>
    /// Helper: Get fleet-wide alert statistics.
    /// </summary>
    private async Task<AlertStatistics> GetAlertStatisticsAsync()
    {
        var activeAlerts = await _context.DeviceHealthAlerts
            .Where(a => a.ResolvedAt == null)
            .ToListAsync();

        return new AlertStatistics
        {
            CriticalCount = activeAlerts.Count(a => a.Severity == AlertSeverity.Critical),
            WarningCount = activeAlerts.Count(a => a.Severity == AlertSeverity.Warning),
            InfoCount = activeAlerts.Count(a => a.Severity == AlertSeverity.Info),
            TotalActiveAlerts = activeAlerts.Count
        };
    }

    /// <summary>
    /// Get comprehensive fleet health metrics (optimized with caching).
    /// GET /api/fleet/health/metrics
    /// Returns cached result if available, with cache info included.
    /// </summary>
    [HttpGet("/api/fleet/health/metrics")]
    public async Task<ActionResult<FleetHealthMetrics>> GetFleetHealthMetricsAsync()
    {
        var cacheKey = TelometryCacheKeys.FleetHealthMetrics();

        // Check cache first
        if (_cacheService.TryGetValue<FleetHealthMetrics>(cacheKey, out var cachedMetrics) && cachedMetrics != null)
        {
            // Include cache info when returning cached result
            if (cachedMetrics.CacheInfo != null)
                cachedMetrics.CacheInfo.FromCache = true;
            
            return Ok(cachedMetrics);
        }

        // Compute fresh metrics
        var totalDevices = await _context.Devices.CountAsync();
        var latestTelemetries = await _context.DeviceTelemetries
            .GroupBy(t => t.DeviceId)
            .Select(g => g.OrderByDescending(t => t.CreatedAt).First())
            .ToListAsync();

        if (latestTelemetries.Count == 0)
        {
            var emptyMetrics = new FleetHealthMetrics
            {
                ComputedAt = DateTimeOffset.UtcNow,
                TotalDevices = totalDevices,
                OnlineDevices = 0,
                OfflineDevices = 0,
                OnlinePercentage = 0,
                AverageBatteryLevel = 0
            };
            return Ok(emptyMetrics);
        }

        var onlineDevices = latestTelemetries.Count(t => t.Connectivity != ConnectivityStatus.Offline);
        var offlineDevices = totalDevices - onlineDevices;

        var avgBattery = (int)latestTelemetries.Average(t => t.BatteryLevel);
        var lowBatteryDevices = latestTelemetries.Count(t => t.BatteryLevel <= 20);
        var criticalBatteryDevices = latestTelemetries.Count(t => t.BatteryLevel <= 5);

        var avgStorageUsed = latestTelemetries.Average(t => 
            t.StorageTotalMb > 0 ? (t.StorageUsedMb * 100.0) / t.StorageTotalMb : 0);
        var avgMemoryUsed = latestTelemetries.Average(t => 
            t.TotalMemoryMb > 0 ? ((t.TotalMemoryMb - t.AvailableMemoryMb) * 100.0) / t.TotalMemoryMb : 0);
        var avgCpuUsage = (int)latestTelemetries.Average(t => t.CpuUsagePercent);

        var lowStorageDevices = latestTelemetries.Count(t => 
            t.StorageTotalMb > 0 && (t.StorageFreeMb * 100.0) / t.StorageTotalMb <= 10);

        var connectivityMetrics = new ConnectivityBreakdownMetrics
        {
            WiFiDevices = latestTelemetries.Count(t => t.Connectivity == ConnectivityStatus.WiFi),
            CellularDevices = latestTelemetries.Count(t => t.Connectivity == ConnectivityStatus.Cellular),
            OfflineDevices = latestTelemetries.Count(t => t.Connectivity == ConnectivityStatus.Offline),
            AirplaneModeDevices = latestTelemetries.Count(t => t.Connectivity == ConnectivityStatus.AirplaneMode),
            UnknownDevices = latestTelemetries.Count(t => t.Connectivity == ConnectivityStatus.Unknown)
        };

        // Calculate percentages
        if (latestTelemetries.Count > 0)
        {
            connectivityMetrics.WiFiPercentage = (connectivityMetrics.WiFiDevices * 100.0) / latestTelemetries.Count;
            connectivityMetrics.CellularPercentage = (connectivityMetrics.CellularDevices * 100.0) / latestTelemetries.Count;
            connectivityMetrics.OfflinePercentage = (connectivityMetrics.OfflineDevices * 100.0) / latestTelemetries.Count;
            connectivityMetrics.AirplaneModePercentage = (connectivityMetrics.AirplaneModeDevices * 100.0) / latestTelemetries.Count;
            connectivityMetrics.UnknownPercentage = (connectivityMetrics.UnknownDevices * 100.0) / latestTelemetries.Count;
        }

        // Get active alerts
        var activeAlerts = await _context.DeviceHealthAlerts
            .Where(a => a.ResolvedAt == null)
            .ToListAsync();

        var devicesWithAlerts = activeAlerts.Select(a => a.DeviceId).Distinct().Count();
        var totalAlerts = activeAlerts.Count;

        var alertMetrics = new HealthAlertMetrics
        {
            CriticalAlerts = activeAlerts.Count(a => a.Severity == AlertSeverity.Critical),
            WarningAlerts = activeAlerts.Count(a => a.Severity == AlertSeverity.Warning),
            InfoAlerts = activeAlerts.Count(a => a.Severity == AlertSeverity.Info),
            TotalActiveAlerts = totalAlerts,
            DevicesWithCriticalAlerts = activeAlerts.Where(a => a.Severity == AlertSeverity.Critical).Select(a => a.DeviceId).Distinct().Count(),
            DevicesWithAnyAlert = devicesWithAlerts
        };

        if (totalAlerts > 0)
        {
            alertMetrics.CriticalAlertsPercentage = (alertMetrics.CriticalAlerts * 100.0) / totalAlerts;
            alertMetrics.WarningAlertsPercentage = (alertMetrics.WarningAlerts * 100.0) / totalAlerts;
            alertMetrics.InfoAlertsPercentage = (alertMetrics.InfoAlerts * 100.0) / totalAlerts;
        }

        // Calculate health score (0-100, where 100 = no alerts)
        var healthScore = 100 - Math.Min(100, (devicesWithAlerts * 100) / Math.Max(1, totalDevices));
        alertMetrics.HealthScore = Math.Max(0, healthScore);

        var metrics = new FleetHealthMetrics
        {
            ComputedAt = DateTimeOffset.UtcNow,
            TotalDevices = totalDevices,
            OnlineDevices = onlineDevices,
            OfflineDevices = offlineDevices,
            OnlinePercentage = totalDevices > 0 ? (onlineDevices * 100.0) / totalDevices : 0,
            AverageBatteryLevel = avgBattery,
            LowBatteryDevices = lowBatteryDevices,
            LowBatteryPercentage = latestTelemetries.Count > 0 ? (lowBatteryDevices * 100.0) / latestTelemetries.Count : 0,
            CriticalBatteryDevices = criticalBatteryDevices,
            AverageStorageUsedPercent = (int)avgStorageUsed,
            LowStorageDevices = lowStorageDevices,
            AverageMemoryUsedPercent = (int)avgMemoryUsed,
            AverageCpuUsagePercent = avgCpuUsage,
            Connectivity = connectivityMetrics,
            Alerts = alertMetrics
        };

        // Cache the metrics
        var ttlSeconds = 300; // 5 minutes default
        _cacheService.Set(cacheKey, metrics, ttlSeconds);

        return Ok(metrics);
    }
}

/// <summary>
/// Generic paginated result wrapper.
/// </summary>
public class PaginatedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

/// <summary>
/// Fleet-wide health summary.
/// </summary>
public class FleetHealthSummary
{
    public int TotalDevices { get; set; }
    public int OnlineDevices { get; set; }
    public int OfflineDevices { get; set; }

    public int AverageBatteryLevel { get; set; }
    public int LowBatteryDevices { get; set; }

    public int AverageStorageUsedPercent { get; set; }
    public int LowStorageDevices { get; set; }

    public ConnectivityBreakdown? Connectivity { get; set; }
    public AlertStatistics? AlertStats { get; set; }

    public DateTimeOffset LastUpdated { get; set; }
}

/// <summary>
/// Connectivity breakdown across fleet.
/// </summary>
public class ConnectivityBreakdown
{
    public int WiFi { get; set; }
    public int Cellular { get; set; }
    public int Offline { get; set; }
    public int AirplaneMode { get; set; }
    public int Unknown { get; set; }
}

/// <summary>
/// Alert statistics for fleet or device.
/// </summary>
public class AlertStatistics
{
    public int CriticalCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public int TotalActiveAlerts { get; set; }
}

/// <summary>
/// Device at risk with critical alerts.
/// </summary>
public class AtRiskDevice
{
    public Guid DeviceId { get; set; }
    public string DeviceName { get; set; } = null!;
    public int CriticalAlertCount { get; set; }
    public List<HealthAlertDto> Alerts { get; set; } = new();
    public int LatestBatteryLevel { get; set; }
    public ConnectivityStatus LatestConnectivity { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
}

/// <summary>
/// Request to resolve an alert.
/// </summary>
public class ResolveAlertRequest
{
    public string? Reason { get; set; }
}
