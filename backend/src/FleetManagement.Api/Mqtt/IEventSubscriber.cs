using System.Text.Json.Serialization;

namespace FleetManagement.Api.Mqtt;

/// <summary>
/// Represents a device acknowledgement event from MQTT.
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
/// Subscribes to device acknowledgements via MQTT.
/// </summary>
public interface IEventSubscriber
{
    /// <summary>
    /// Registers a handler to be called when a device acknowledgement is received.
    /// </summary>
    /// <param name="handler">The handler to call for each acknowledgement.</param>
    /// <returns>A disposable that removes the handler when disposed.</returns>
    IDisposable OnAcknowledgement(Func<DeviceAcknowledgement, Task> handler);
}
