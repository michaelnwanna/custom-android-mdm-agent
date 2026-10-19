namespace FleetManagement.Devices;

using System;

/// <summary>
/// Represents a device telemetry snapshot.
/// Records device health metrics at a point in time: battery, storage, memory, connectivity.
/// </summary>
public class DeviceTelemetry
{
    /// <summary>
    /// Unique telemetry record identifier.
    /// </summary>
    public Guid TelemetryId { get; private set; }

    /// <summary>
    /// Reference to the device this telemetry belongs to.
    /// </summary>
    public Guid DeviceId { get; private set; }

    /// <summary>
    /// Battery level as a percentage (0-100).
    /// 0 = empty, 100 = fully charged.
    /// </summary>
    public int BatteryLevel { get; private set; }

    /// <summary>
    /// Whether the device is currently charging (plugged in).
    /// </summary>
    public bool IsCharging { get; private set; }

    /// <summary>
    /// Storage used on device in MB.
    /// </summary>
    public long StorageUsedMb { get; private set; }

    /// <summary>
    /// Total storage capacity of device in MB.
    /// </summary>
    public long StorageTotalMb { get; private set; }

    /// <summary>
    /// Available free storage in MB.
    /// Calculated as StorageTotalMb - StorageUsedMb.
    /// </summary>
    public long StorageFreeMb { get; private set; }

    /// <summary>
    /// Available RAM in MB (free + cached memory).
    /// </summary>
    public long AvailableMemoryMb { get; private set; }

    /// <summary>
    /// Total RAM capacity in MB.
    /// </summary>
    public long TotalMemoryMb { get; private set; }

    /// <summary>
    /// Device connectivity state.
    /// </summary>
    public ConnectivityStatus Connectivity { get; private set; }

    /// <summary>
    /// WiFi signal strength in dBm (-127 to -25).
    /// Null if not connected to WiFi.
    /// </summary>
    public int? WifiSignalStrengthDbm { get; private set; }

    /// <summary>
    /// Cellular signal strength in dBm (-127 to -25).
    /// Null if no cellular signal.
    /// </summary>
    public int? CellularSignalStrengthDbm { get; private set; }

    /// <summary>
    /// CPU usage as a percentage (0-100).
    /// Snapshot at time of telemetry collection.
    /// </summary>
    public int CpuUsagePercent { get; private set; }

    /// <summary>
    /// Timestamp of when device last reported in.
    /// Used for offline detection (if LastCheckInAt is stale).
    /// </summary>
    public DateTimeOffset LastCheckInAt { get; private set; }

    /// <summary>
    /// Timestamp when this telemetry snapshot was created on device.
    /// </summary>
    public DateTimeOffset SnapshotTakenAt { get; private set; }

    /// <summary>
    /// Timestamp when this telemetry was recorded in the system.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    // Navigation properties
    /// <summary>
    /// Reference to the device.
    /// </summary>
    public Device? Device { get; private set; }

    private DeviceTelemetry() { }

    /// <summary>
    /// Create a new device telemetry record.
    /// </summary>
    public static DeviceTelemetry Create(
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
        DateTimeOffset lastCheckInAt,
        DateTimeOffset snapshotTakenAt)
    {
        // Validate ranges
        if (batteryLevel < 0 || batteryLevel > 100)
            throw new ArgumentException("Battery level must be between 0 and 100", nameof(batteryLevel));

        if (cpuUsagePercent < 0 || cpuUsagePercent > 100)
            throw new ArgumentException("CPU usage must be between 0 and 100", nameof(cpuUsagePercent));

        if (storageUsedMb < 0 || storageTotalMb < 0 || availableMemoryMb < 0 || totalMemoryMb < 0)
            throw new ArgumentException("Storage and memory values cannot be negative", nameof(storageUsedMb));

        if (wifiSignalStrengthDbm.HasValue && (wifiSignalStrengthDbm < -127 || wifiSignalStrengthDbm > -25))
            throw new ArgumentException("WiFi signal strength must be between -127 and -25 dBm", nameof(wifiSignalStrengthDbm));

        if (cellularSignalStrengthDbm.HasValue && (cellularSignalStrengthDbm < -127 || cellularSignalStrengthDbm > -25))
            throw new ArgumentException("Cellular signal strength must be between -127 and -25 dBm", nameof(cellularSignalStrengthDbm));

        var now = DateTimeOffset.UtcNow;
        return new DeviceTelemetry
        {
            TelemetryId = Guid.NewGuid(),
            DeviceId = deviceId,
            BatteryLevel = batteryLevel,
            IsCharging = isCharging,
            StorageUsedMb = storageUsedMb,
            StorageTotalMb = storageTotalMb,
            StorageFreeMb = storageTotalMb - storageUsedMb,
            AvailableMemoryMb = availableMemoryMb,
            TotalMemoryMb = totalMemoryMb,
            Connectivity = connectivity,
            WifiSignalStrengthDbm = wifiSignalStrengthDbm,
            CellularSignalStrengthDbm = cellularSignalStrengthDbm,
            CpuUsagePercent = cpuUsagePercent,
            LastCheckInAt = lastCheckInAt,
            SnapshotTakenAt = snapshotTakenAt,
            CreatedAt = now
        };
    }
}

/// <summary>
/// Device connectivity status.
/// </summary>
public enum ConnectivityStatus
{
    /// <summary>Device is offline (no network connectivity).</summary>
    Offline = 0,

    /// <summary>Device is connected via WiFi.</summary>
    WiFi = 1,

    /// <summary>Device is connected via cellular network.</summary>
    Cellular = 2,

    /// <summary>Device is in airplane mode.</summary>
    AirplaneMode = 3,

    /// <summary>Unknown connectivity status.</summary>
    Unknown = 4
}

/// <summary>
/// Represents a health alert triggered by telemetry thresholds.
/// Alerts are created when device metrics exceed configured thresholds (e.g., low battery).
/// </summary>
public class DeviceHealthAlert
{
    /// <summary>
    /// Unique alert identifier.
    /// </summary>
    public Guid AlertId { get; private set; }

    /// <summary>
    /// Reference to the device that triggered this alert.
    /// </summary>
    public Guid DeviceId { get; private set; }

    /// <summary>
    /// Type of health alert.
    /// </summary>
    public HealthAlertType AlertType { get; private set; }

    /// <summary>
    /// Severity level of the alert.
    /// </summary>
    public AlertSeverity Severity { get; private set; }

    /// <summary>
    /// Human-readable message describing the alert.
    /// Example: "Battery is 15%, below 20% threshold".
    /// </summary>
    public string Message { get; private set; } = null!;

    /// <summary>
    /// Telemetry data that triggered this alert (reference).
    /// Allows tracing alert back to specific measurement.
    /// </summary>
    public Guid? TelemetryId { get; private set; }

    /// <summary>
    /// Timestamp when alert was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Timestamp when alert was resolved or cleared.
    /// Null while alert is active.
    /// </summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>
    /// Reason for alert resolution.
    /// Example: "Battery reached 25%", "Manually cleared".
    /// Null if not resolved.
    /// </summary>
    public string? ResolutionReason { get; private set; }

    // Navigation properties
    /// <summary>
    /// Reference to the device.
    /// </summary>
    public Device? Device { get; private set; }

    /// <summary>
    /// Reference to the telemetry that triggered this alert.
    /// </summary>
    public DeviceTelemetry? Telemetry { get; private set; }

    private DeviceHealthAlert() { }

    /// <summary>
    /// Create a new health alert.
    /// </summary>
    public static DeviceHealthAlert Create(
        Guid deviceId,
        HealthAlertType alertType,
        AlertSeverity severity,
        string message,
        Guid? telemetryId = null)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Alert message cannot be empty", nameof(message));

        var now = DateTimeOffset.UtcNow;
        return new DeviceHealthAlert
        {
            AlertId = Guid.NewGuid(),
            DeviceId = deviceId,
            AlertType = alertType,
            Severity = severity,
            Message = message,
            TelemetryId = telemetryId,
            CreatedAt = now,
            ResolvedAt = null,
            ResolutionReason = null
        };
    }

    /// <summary>
    /// Resolve this alert with a reason.
    /// </summary>
    public void Resolve(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Resolution reason cannot be empty", nameof(reason));

        ResolvedAt = DateTimeOffset.UtcNow;
        ResolutionReason = reason;
    }

    /// <summary>
    /// Check if this alert is currently active (not resolved).
    /// </summary>
    public bool IsActive => ResolvedAt == null;
}

/// <summary>
/// Types of health alerts that can be triggered.
/// </summary>
public enum HealthAlertType
{
    /// <summary>Battery level is critically low.</summary>
    LowBattery = 0,

    /// <summary>Device storage is critically low.</summary>
    LowStorage = 1,

    /// <summary>Device memory usage is very high.</summary>
    HighMemory = 2,

    /// <summary>Device has no network connectivity.</summary>
    NoConnectivity = 3,

    /// <summary>Device has not checked in for an extended period.</summary>
    DeviceOffline = 4,

    /// <summary>CPU usage is abnormally high.</summary>
    HighCpuUsage = 5,

    /// <summary>Weak wireless signal.</summary>
    WeakSignal = 6
}

/// <summary>
/// Alert severity levels.
/// </summary>
public enum AlertSeverity
{
    /// <summary>Informational alert, no action required.</summary>
    Info = 0,

    /// <summary>Warning alert, monitor situation.</summary>
    Warning = 1,

    /// <summary>Critical alert, immediate action may be needed.</summary>
    Critical = 2
}
