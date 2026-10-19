namespace FleetManagement.Devices;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Configuration for telemetry health thresholds.
/// These determine when health alerts are triggered.
/// </summary>
public class TelemetryThresholdSettings
{
    /// <summary>
    /// Battery level below which LowBattery alerts are triggered (default: 20%).
    /// </summary>
    public int LowBatteryThresholdPercent { get; set; } = 20;

    /// <summary>
    /// Battery level that triggers a critical alert (default: 5%).
    /// </summary>
    public int CriticalBatteryThresholdPercent { get; set; } = 5;

    /// <summary>
    /// Free storage percentage below which LowStorage alerts are triggered (default: 10%).
    /// </summary>
    public int LowStorageThresholdPercent { get; set; } = 10;

    /// <summary>
    /// Memory usage percentage above which HighMemory alerts are triggered (default: 85%).
    /// </summary>
    public int HighMemoryThresholdPercent { get; set; } = 85;

    /// <summary>
    /// CPU usage percentage above which HighCpuUsage alerts are triggered (default: 85%).
    /// </summary>
    public int HighCpuUsageThresholdPercent { get; set; } = 85;

    /// <summary>
    /// Signal strength (dBm) below which WeakSignal alerts are triggered (default: -80 dBm).
    /// Lower values indicate weaker signal.
    /// </summary>
    public int WeakSignalThresholdDbm { get; set; } = -80;

    /// <summary>
    /// Minutes without device check-in before DeviceOffline alerts are triggered (default: 30 minutes).
    /// </summary>
    public int DeviceOfflineThresholdMinutes { get; set; } = 30;

    /// <summary>
    /// Number of days to retain telemetry records (default: 30 days).
    /// Older records are deleted during cleanup.
    /// </summary>
    public int TelemetryRetentionDays { get; set; } = 30;

    /// <summary>
    /// Number of days to retain resolved alerts (default: 30 days).
    /// </summary>
    public int ResolvedAlertRetentionDays { get; set; } = 30;
}

/// <summary>
/// Service for collecting, storing, and analyzing device telemetry data.
/// Responsible for ingesting telemetry from devices and triggering health alerts.
/// </summary>
public class TelemetryCollectionService
{
    private readonly FleetDbContext _context;
    private readonly TelemetryThresholdSettings _thresholds;

    public TelemetryCollectionService(FleetDbContext context, TelemetryThresholdSettings? thresholds = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _thresholds = thresholds ?? new TelemetryThresholdSettings();
    }

    /// <summary>
    /// Record a new telemetry snapshot from a device.
    /// Also checks health thresholds and generates alerts if needed.
    /// </summary>
    public async Task<RecordTelemetryResult> RecordTelemetryAsync(
        Guid deviceId,
        int batteryLevel,
        bool isCharging,
        long storageUsedMb,
        long storageTotalMb,
        long availableMemoryMb,
        long totalMemoryMb,
        ConnectivityStatus connectivity,
        int? wifiSignalStrengthDbm,
        int? cellularSignalStrengthDbm,
        int cpuUsagePercent,
        DateTimeOffset? snapshotTakenAt = null)
    {
        snapshotTakenAt ??= DateTimeOffset.UtcNow;

        // Verify device exists
        var device = await _context.Devices.FirstOrDefaultAsync(d => d.DeviceId == deviceId);
        if (device == null)
            throw new InvalidOperationException($"Device {deviceId} not found");

        // Update device's last seen timestamp
        device.UpdateLastSeen();

        // Create telemetry record
        var telemetry = DeviceTelemetry.Create(
            deviceId,
            batteryLevel,
            isCharging,
            storageUsedMb,
            storageTotalMb,
            availableMemoryMb,
            totalMemoryMb,
            connectivity,
            wifiSignalStrengthDbm,
            cellularSignalStrengthDbm,
            cpuUsagePercent,
            DateTimeOffset.UtcNow,
            snapshotTakenAt.Value);

        _context.DeviceTelemetries.Add(telemetry);

        // Check health thresholds and generate alerts
        var alertsGenerated = await CheckHealthThresholdsAsync(device, telemetry);

        await _context.SaveChangesAsync();

        return new RecordTelemetryResult
        {
            TelemetryId = telemetry.TelemetryId,
            DeviceId = deviceId,
            AlertsGenerated = alertsGenerated,
            BatteryLevel = batteryLevel,
            Connectivity = connectivity,
            RecordedAt = telemetry.CreatedAt
        };
    }

    /// <summary>
    /// Check device telemetry against health thresholds and generate alerts.
    /// </summary>
    private async Task<List<Guid>> CheckHealthThresholdsAsync(Device device, DeviceTelemetry telemetry)
    {
        var alertIds = new List<Guid>();

        // Check battery level
        if (telemetry.BatteryLevel <= _thresholds.CriticalBatteryThresholdPercent && !telemetry.IsCharging)
        {
            var alert = await GenerateAlertIfNeeded(
                device.DeviceId,
                HealthAlertType.LowBattery,
                AlertSeverity.Critical,
                $"Battery critically low: {telemetry.BatteryLevel}%, charging: {telemetry.IsCharging}",
                telemetry.TelemetryId);
            if (alert != null)
                alertIds.Add(alert.AlertId);
        }
        else if (telemetry.BatteryLevel <= _thresholds.LowBatteryThresholdPercent && !telemetry.IsCharging)
        {
            var alert = await GenerateAlertIfNeeded(
                device.DeviceId,
                HealthAlertType.LowBattery,
                AlertSeverity.Warning,
                $"Battery low: {telemetry.BatteryLevel}%, charging: {telemetry.IsCharging}",
                telemetry.TelemetryId);
            if (alert != null)
                alertIds.Add(alert.AlertId);
        }

        // Check storage
        if (telemetry.StorageTotalMb > 0)
        {
            var freeStoragePercent = (telemetry.StorageFreeMb * 100.0) / telemetry.StorageTotalMb;
            if (freeStoragePercent <= _thresholds.LowStorageThresholdPercent)
            {
                var alert = await GenerateAlertIfNeeded(
                    device.DeviceId,
                    HealthAlertType.LowStorage,
                    AlertSeverity.Warning,
                    $"Low storage: {freeStoragePercent:F1}% free ({telemetry.StorageFreeMb}MB of {telemetry.StorageTotalMb}MB)",
                    telemetry.TelemetryId);
                if (alert != null)
                    alertIds.Add(alert.AlertId);
            }
        }

        // Check memory
        if (telemetry.TotalMemoryMb > 0)
        {
            var usedMemoryPercent = ((telemetry.TotalMemoryMb - telemetry.AvailableMemoryMb) * 100.0) / telemetry.TotalMemoryMb;
            if (usedMemoryPercent >= _thresholds.HighMemoryThresholdPercent)
            {
                var alert = await GenerateAlertIfNeeded(
                    device.DeviceId,
                    HealthAlertType.HighMemory,
                    AlertSeverity.Warning,
                    $"High memory usage: {usedMemoryPercent:F1}% ({telemetry.TotalMemoryMb - telemetry.AvailableMemoryMb}MB of {telemetry.TotalMemoryMb}MB)",
                    telemetry.TelemetryId);
                if (alert != null)
                    alertIds.Add(alert.AlertId);
            }
        }

        // Check CPU usage
        if (telemetry.CpuUsagePercent >= _thresholds.HighCpuUsageThresholdPercent)
        {
            var alert = await GenerateAlertIfNeeded(
                device.DeviceId,
                HealthAlertType.HighCpuUsage,
                AlertSeverity.Info,
                $"High CPU usage: {telemetry.CpuUsagePercent}%",
                telemetry.TelemetryId);
            if (alert != null)
                alertIds.Add(alert.AlertId);
        }

        // Check connectivity
        if (telemetry.Connectivity == ConnectivityStatus.Offline)
        {
            var alert = await GenerateAlertIfNeeded(
                device.DeviceId,
                HealthAlertType.NoConnectivity,
                AlertSeverity.Warning,
                "Device is offline",
                telemetry.TelemetryId);
            if (alert != null)
                alertIds.Add(alert.AlertId);
        }

        // Check signal strength
        var signalDbm = telemetry.Connectivity == ConnectivityStatus.WiFi ? telemetry.WifiSignalStrengthDbm : telemetry.CellularSignalStrengthDbm;
        if (signalDbm.HasValue && signalDbm < _thresholds.WeakSignalThresholdDbm)
        {
            var networkType = telemetry.Connectivity == ConnectivityStatus.WiFi ? "WiFi" : "Cellular";
            var alert = await GenerateAlertIfNeeded(
                device.DeviceId,
                HealthAlertType.WeakSignal,
                AlertSeverity.Info,
                $"Weak {networkType} signal: {signalDbm} dBm",
                telemetry.TelemetryId);
            if (alert != null)
                alertIds.Add(alert.AlertId);
        }

        return alertIds;
    }

    /// <summary>
    /// Generate a health alert if one doesn't already exist for this device and alert type.
    /// Returns null if alert already exists (to avoid duplicates).
    /// </summary>
    private async Task<DeviceHealthAlert?> GenerateAlertIfNeeded(
        Guid deviceId,
        HealthAlertType alertType,
        AlertSeverity severity,
        string message,
        Guid? telemetryId)
    {
        // Check if an active alert already exists for this device and type
        var existingAlert = await _context.DeviceHealthAlerts
            .Where(a => a.DeviceId == deviceId && a.AlertType == alertType && a.ResolvedAt == null)
            .FirstOrDefaultAsync();

        if (existingAlert != null)
            return null; // Alert already exists

        var alert = DeviceHealthAlert.Create(deviceId, alertType, severity, message, telemetryId);
        _context.DeviceHealthAlerts.Add(alert);
        return alert;
    }

    /// <summary>
    /// Get the latest telemetry snapshot for a device.
    /// </summary>
    public async Task<DeviceTelemetry?> GetLatestTelemetryAsync(Guid deviceId)
    {
        return await _context.DeviceTelemetries
            .Where(t => t.DeviceId == deviceId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Get average device health metrics over a time period.
    /// </summary>
    public async Task<AggregatedDeviceHealth?> AggregateDeviceHealthAsync(Guid deviceId, int? hoursBack = null)
    {
        hoursBack ??= 24; // Default to last 24 hours

        var cutoffTime = DateTimeOffset.UtcNow.AddHours(-hoursBack.Value);

        var telemetries = await _context.DeviceTelemetries
            .Where(t => t.DeviceId == deviceId && t.CreatedAt >= cutoffTime)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();

        if (telemetries.Count == 0)
            return null;

        return new AggregatedDeviceHealth
        {
            DeviceId = deviceId,
            DataPointsCount = telemetries.Count,
            TimeSpanHours = hoursBack.Value,
            AverageBatteryLevel = (int)telemetries.Average(t => t.BatteryLevel),
            MinBatteryLevel = telemetries.Min(t => t.BatteryLevel),
            MaxBatteryLevel = telemetries.Max(t => t.BatteryLevel),
            AverageStorageUsedPercent = telemetries.Count > 0
                ? (int)(telemetries.Average(t => t.StorageTotalMb > 0 ? (t.StorageUsedMb * 100.0) / t.StorageTotalMb : 0))
                : 0,
            AverageMemoryUsedPercent = telemetries.Count > 0
                ? (int)(telemetries.Average(t => t.TotalMemoryMb > 0
                    ? ((t.TotalMemoryMb - t.AvailableMemoryMb) * 100.0) / t.TotalMemoryMb
                    : 0))
                : 0,
            AverageCpuUsagePercent = (int)telemetries.Average(t => t.CpuUsagePercent),
            DominantConnectivity = telemetries.GroupBy(t => t.Connectivity)
                .OrderByDescending(g => g.Count())
                .First()
                .Key,
            HighestCpuUsagePercent = telemetries.Max(t => t.CpuUsagePercent),
            WifiConnectionCount = telemetries.Count(t => t.Connectivity == ConnectivityStatus.WiFi),
            CellularConnectionCount = telemetries.Count(t => t.Connectivity == ConnectivityStatus.Cellular),
            OfflineCount = telemetries.Count(t => t.Connectivity == ConnectivityStatus.Offline),
            FirstDataPoint = telemetries.First().CreatedAt,
            LastDataPoint = telemetries.Last().CreatedAt
        };
    }

    /// <summary>
    /// Get active health alerts for a device.
    /// </summary>
    public async Task<List<DeviceHealthAlert>> GetActiveAlertsAsync(Guid deviceId)
    {
        return await _context.DeviceHealthAlerts
            .Where(a => a.DeviceId == deviceId && a.ResolvedAt == null)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Resolve an active alert with a reason.
    /// </summary>
    public async Task<bool> ResolveAlertAsync(Guid alertId, string reason)
    {
        var alert = await _context.DeviceHealthAlerts.FirstOrDefaultAsync(a => a.AlertId == alertId);
        if (alert == null || alert.ResolvedAt != null)
            return false;

        alert.Resolve(reason);
        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Automatically resolve alerts when conditions improve.
    /// For example: resolve LowBattery alert when battery reaches safe level.
    /// </summary>
    public async Task<int> AutoResolveAlertsAsync(Guid deviceId, DeviceTelemetry latestTelemetry)
    {
        var activeAlerts = await GetActiveAlertsAsync(deviceId);
        int resolvedCount = 0;

        foreach (var alert in activeAlerts)
        {
            bool shouldResolve = false;
            string? reason = null;

            switch (alert.AlertType)
            {
                case HealthAlertType.LowBattery when latestTelemetry.BatteryLevel > (_thresholds.LowBatteryThresholdPercent + 10) && latestTelemetry.IsCharging:
                    shouldResolve = true;
                    reason = $"Battery recovered to {latestTelemetry.BatteryLevel}%";
                    break;

                case HealthAlertType.LowStorage when latestTelemetry.StorageTotalMb > 0:
                    var freePercent = (latestTelemetry.StorageFreeMb * 100.0) / latestTelemetry.StorageTotalMb;
                    if (freePercent > (_thresholds.LowStorageThresholdPercent + 10))
                    {
                        shouldResolve = true;
                        reason = $"Storage freed to {freePercent:F1}%";
                    }
                    break;

                case HealthAlertType.HighMemory when latestTelemetry.TotalMemoryMb > 0:
                    var usedPercent = ((latestTelemetry.TotalMemoryMb - latestTelemetry.AvailableMemoryMb) * 100.0) / latestTelemetry.TotalMemoryMb;
                    if (usedPercent < (_thresholds.HighMemoryThresholdPercent - 10))
                    {
                        shouldResolve = true;
                        reason = $"Memory usage decreased to {usedPercent:F1}%";
                    }
                    break;

                case HealthAlertType.HighCpuUsage when latestTelemetry.CpuUsagePercent < (_thresholds.HighCpuUsageThresholdPercent - 10):
                    shouldResolve = true;
                    reason = $"CPU usage decreased to {latestTelemetry.CpuUsagePercent}%";
                    break;

                case HealthAlertType.NoConnectivity when latestTelemetry.Connectivity != ConnectivityStatus.Offline:
                    shouldResolve = true;
                    reason = $"Device reconnected via {latestTelemetry.Connectivity}";
                    break;
            }

            if (shouldResolve && reason != null)
            {
                alert.Resolve(reason);
                resolvedCount++;
            }
        }

        if (resolvedCount > 0)
            await _context.SaveChangesAsync();

        return resolvedCount;
    }

    /// <summary>
    /// Clean up old telemetry records and resolved alerts.
    /// Called periodically (e.g., daily) to manage database size.
    /// </summary>
    public async Task<int> CleanupOldDataAsync(int? telemetryDaysToKeep = null, int? alertDaysToKeep = null)
    {
        telemetryDaysToKeep ??= _thresholds.TelemetryRetentionDays;
        alertDaysToKeep ??= _thresholds.ResolvedAlertRetentionDays;

        var telemetryCutoff = DateTimeOffset.UtcNow.AddDays(-telemetryDaysToKeep.Value);
        var alertCutoff = DateTimeOffset.UtcNow.AddDays(-alertDaysToKeep.Value);

        // Delete old telemetry records
        var oldTelemetry = await _context.DeviceTelemetries
            .Where(t => t.CreatedAt < telemetryCutoff)
            .ToListAsync();

        _context.DeviceTelemetries.RemoveRange(oldTelemetry);

        // Delete old resolved alerts
        var oldAlerts = await _context.DeviceHealthAlerts
            .Where(a => a.ResolvedAt != null && a.ResolvedAt < alertCutoff)
            .ToListAsync();

        _context.DeviceHealthAlerts.RemoveRange(oldAlerts);

        await _context.SaveChangesAsync();

        return oldTelemetry.Count + oldAlerts.Count;
    }
}

/// <summary>
/// Result of recording a telemetry data point.
/// </summary>
public class RecordTelemetryResult
{
    public Guid TelemetryId { get; set; }
    public Guid DeviceId { get; set; }
    public List<Guid> AlertsGenerated { get; set; } = new();
    public int BatteryLevel { get; set; }
    public ConnectivityStatus Connectivity { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>
/// Aggregated health metrics for a device over a time period.
/// </summary>
public class AggregatedDeviceHealth
{
    public Guid DeviceId { get; set; }
    public int DataPointsCount { get; set; }
    public int TimeSpanHours { get; set; }

    public int AverageBatteryLevel { get; set; }
    public int MinBatteryLevel { get; set; }
    public int MaxBatteryLevel { get; set; }

    public int AverageStorageUsedPercent { get; set; }
    public int AverageMemoryUsedPercent { get; set; }
    public int AverageCpuUsagePercent { get; set; }
    public int HighestCpuUsagePercent { get; set; }

    public ConnectivityStatus DominantConnectivity { get; set; }
    public int WifiConnectionCount { get; set; }
    public int CellularConnectionCount { get; set; }
    public int OfflineCount { get; set; }

    public DateTimeOffset FirstDataPoint { get; set; }
    public DateTimeOffset LastDataPoint { get; set; }
}
