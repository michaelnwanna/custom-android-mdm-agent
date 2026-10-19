namespace FleetManagement.Devices;

using System;

/// <summary>
/// DTO for receiving telemetry data from devices.
/// Used in POST /api/telemetry/report endpoint.
/// </summary>
public class SubmitTelemetryRequest
{
    /// <summary>
    /// Device ID submitting the telemetry.
    /// </summary>
    public Guid DeviceId { get; set; }

    /// <summary>
    /// Battery level as a percentage (0-100).
    /// </summary>
    public int BatteryLevel { get; set; }

    /// <summary>
    /// Whether the device is currently charging.
    /// </summary>
    public bool IsCharging { get; set; }

    /// <summary>
    /// Storage used in MB.
    /// </summary>
    public long StorageUsedMb { get; set; }

    /// <summary>
    /// Total storage capacity in MB.
    /// </summary>
    public long StorageTotalMb { get; set; }

    /// <summary>
    /// Available RAM in MB.
    /// </summary>
    public long AvailableMemoryMb { get; set; }

    /// <summary>
    /// Total RAM capacity in MB.
    /// </summary>
    public long TotalMemoryMb { get; set; }

    /// <summary>
    /// Device connectivity status.
    /// </summary>
    public ConnectivityStatus Connectivity { get; set; }

    /// <summary>
    /// WiFi signal strength in dBm (-127 to -25). Null if not connected to WiFi.
    /// </summary>
    public int? WifiSignalStrengthDbm { get; set; }

    /// <summary>
    /// Cellular signal strength in dBm (-127 to -25). Null if no cellular signal.
    /// </summary>
    public int? CellularSignalStrengthDbm { get; set; }

    /// <summary>
    /// CPU usage as a percentage (0-100).
    /// </summary>
    public int CpuUsagePercent { get; set; }

    /// <summary>
    /// When this telemetry snapshot was taken on the device.
    /// If not provided, server time is used.
    /// </summary>
    public DateTimeOffset? SnapshotTakenAt { get; set; }
}

/// <summary>
/// DTO for returning telemetry data to clients.
/// Used in GET endpoints.
/// </summary>
public class TelemetrySnapshot
{
    public Guid TelemetryId { get; set; }
    public Guid DeviceId { get; set; }

    public int BatteryLevel { get; set; }
    public bool IsCharging { get; set; }

    public long StorageUsedMb { get; set; }
    public long StorageTotalMb { get; set; }
    public long StorageFreeMb { get; set; }

    public long AvailableMemoryMb { get; set; }
    public long TotalMemoryMb { get; set; }

    public ConnectivityStatus Connectivity { get; set; }
    public int? WifiSignalStrengthDbm { get; set; }
    public int? CellularSignalStrengthDbm { get; set; }

    public int CpuUsagePercent { get; set; }

    public DateTimeOffset LastCheckInAt { get; set; }
    public DateTimeOffset SnapshotTakenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static TelemetrySnapshot FromDomain(DeviceTelemetry telemetry) => new()
    {
        TelemetryId = telemetry.TelemetryId,
        DeviceId = telemetry.DeviceId,
        BatteryLevel = telemetry.BatteryLevel,
        IsCharging = telemetry.IsCharging,
        StorageUsedMb = telemetry.StorageUsedMb,
        StorageTotalMb = telemetry.StorageTotalMb,
        StorageFreeMb = telemetry.StorageFreeMb,
        AvailableMemoryMb = telemetry.AvailableMemoryMb,
        TotalMemoryMb = telemetry.TotalMemoryMb,
        Connectivity = telemetry.Connectivity,
        WifiSignalStrengthDbm = telemetry.WifiSignalStrengthDbm,
        CellularSignalStrengthDbm = telemetry.CellularSignalStrengthDbm,
        CpuUsagePercent = telemetry.CpuUsagePercent,
        LastCheckInAt = telemetry.LastCheckInAt,
        SnapshotTakenAt = telemetry.SnapshotTakenAt,
        CreatedAt = telemetry.CreatedAt
    };
}

/// <summary>
/// DTO for health alert information.
/// </summary>
public class HealthAlertDto
{
    public Guid AlertId { get; set; }
    public Guid DeviceId { get; set; }
    public HealthAlertType AlertType { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = null!;
    public Guid? TelemetryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolutionReason { get; set; }
    public bool IsActive { get; set; }

    public static HealthAlertDto FromDomain(DeviceHealthAlert alert) => new()
    {
        AlertId = alert.AlertId,
        DeviceId = alert.DeviceId,
        AlertType = alert.AlertType,
        Severity = alert.Severity,
        Message = alert.Message,
        TelemetryId = alert.TelemetryId,
        CreatedAt = alert.CreatedAt,
        ResolvedAt = alert.ResolvedAt,
        ResolutionReason = alert.ResolutionReason,
        IsActive = alert.IsActive
    };
}

/// <summary>
/// DTO for aggregated device health metrics.
/// </summary>
public class AggregatedHealthDto
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

    public static AggregatedHealthDto FromDomain(AggregatedDeviceHealth health) => new()
    {
        DeviceId = health.DeviceId,
        DataPointsCount = health.DataPointsCount,
        TimeSpanHours = health.TimeSpanHours,
        AverageBatteryLevel = health.AverageBatteryLevel,
        MinBatteryLevel = health.MinBatteryLevel,
        MaxBatteryLevel = health.MaxBatteryLevel,
        AverageStorageUsedPercent = health.AverageStorageUsedPercent,
        AverageMemoryUsedPercent = health.AverageMemoryUsedPercent,
        AverageCpuUsagePercent = health.AverageCpuUsagePercent,
        HighestCpuUsagePercent = health.HighestCpuUsagePercent,
        DominantConnectivity = health.DominantConnectivity,
        WifiConnectionCount = health.WifiConnectionCount,
        CellularConnectionCount = health.CellularConnectionCount,
        OfflineCount = health.OfflineCount,
        FirstDataPoint = health.FirstDataPoint,
        LastDataPoint = health.LastDataPoint
    };
}

/// <summary>
/// Response when telemetry is successfully recorded.
/// </summary>
public class RecordTelemetryResponse
{
    public Guid TelemetryId { get; set; }
    public Guid DeviceId { get; set; }
    public int AlertsGeneratedCount { get; set; }
    public List<Guid> AlertIds { get; set; } = new();
    public int BatteryLevel { get; set; }
    public ConnectivityStatus Connectivity { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public bool Success { get; set; }

    public static RecordTelemetryResponse FromResult(RecordTelemetryResult result) => new()
    {
        TelemetryId = result.TelemetryId,
        DeviceId = result.DeviceId,
        AlertsGeneratedCount = result.AlertsGenerated.Count,
        AlertIds = result.AlertsGenerated,
        BatteryLevel = result.BatteryLevel,
        Connectivity = result.Connectivity,
        RecordedAt = result.RecordedAt,
        Success = true
    };
}
