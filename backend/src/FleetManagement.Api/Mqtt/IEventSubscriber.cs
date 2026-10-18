using System.Text.Json.Serialization;

namespace FleetManagement.Api.Mqtt;

/// <summary>
/// Device status values matching the deployment device state machine.
/// </summary>
public enum MqttDeviceStatus
{
    /// <summary>Device received the command and is preparing.</summary>
    Acknowledged = 0,

    /// <summary>Device is downloading the APK file.</summary>
    Downloading = 1,

    /// <summary>Device finished downloading, starting installation.</summary>
    Downloaded = 2,

    /// <summary>Device is installing the APK.</summary>
    Installing = 3,

    /// <summary>Device successfully completed installation.</summary>
    Installed = 4,

    /// <summary>Device encountered an error (see errorMessage).</summary>
    Failed = 5
}

/// <summary>
/// Represents a device status report from MQTT.
/// Includes full status, progress information, and error details.
/// </summary>
public class DeviceStatusReport
{
    /// <summary>
    /// Schema version for the status report event.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    /// <summary>
    /// Device ID that is reporting status.
    /// </summary>
    [JsonPropertyName("deviceId")]
    public Guid DeviceId { get; set; }

    /// <summary>
    /// Command ID being acknowledged/reported.
    /// </summary>
    [JsonPropertyName("commandId")]
    public Guid CommandId { get; set; }

    /// <summary>
    /// Current device status (acknowledged, downloading, installing, installed, failed).
    /// </summary>
    [JsonPropertyName("status")]
    public MqttDeviceStatus Status { get; set; }

    /// <summary>
    /// Download progress as percentage (0-100) when status is Downloading.
    /// </summary>
    [JsonPropertyName("progress")]
    public int? Progress { get; set; }

    /// <summary>
    /// Error message if status is Failed.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Error code for machine-readable error classification.
    /// </summary>
    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    /// <summary>
    /// When the device sent this status report (UTC).
    /// </summary>
    [JsonPropertyName("reportedAt")]
    public DateTimeOffset ReportedAt { get; set; }

    /// <summary>
    /// Time when the device received the original command (UTC).
    /// </summary>
    [JsonPropertyName("receivedAt")]
    public DateTimeOffset? ReceivedAt { get; set; }
}

/// <summary>
/// Backwards-compatible acknowledgement model for legacy events.
/// Represents a simple device acknowledgement event from MQTT.
/// </summary>
public class DeviceAcknowledgement
{
    /// <summary>
    /// Schema version for the acknowledgement event.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    /// <summary>
    /// Device ID that is acknowledging the event.
    /// </summary>
    [JsonPropertyName("deviceId")]
    public Guid DeviceId { get; set; }

    /// <summary>
    /// Event ID (command ID) that was acknowledged.
    /// </summary>
    [JsonPropertyName("eventId")]
    public Guid EventId { get; set; }

    /// <summary>
    /// When the device received the event.
    /// </summary>
    [JsonPropertyName("receivedAt")]
    public DateTimeOffset ReceivedAt { get; set; }
}

/// <summary>
/// Subscribes to device events via MQTT.
/// </summary>
public interface IEventSubscriber
{
    /// <summary>
    /// Registers a handler to be called when a device status report is received.
    /// </summary>
    /// <param name="handler">The handler to call for each status report.</param>
    /// <returns>A disposable that removes the handler when disposed.</returns>
    IDisposable OnStatusReport(Func<DeviceStatusReport, Task> handler);

    /// <summary>
    /// Registers a handler to be called when a device acknowledgement is received (legacy).
    /// </summary>
    /// <param name="handler">The handler to call for each acknowledgement.</param>
    /// <returns>A disposable that removes the handler when disposed.</returns>
    IDisposable OnAcknowledgement(Func<DeviceAcknowledgement, Task> handler);
}
