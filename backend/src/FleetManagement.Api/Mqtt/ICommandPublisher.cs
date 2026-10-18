using FleetManagement.Releases;

namespace FleetManagement.Api.Mqtt;

/// <summary>
/// Publishes commands to devices via MQTT.
/// </summary>
public interface ICommandPublisher
{
    /// <summary>
    /// Publishes an INSTALL_RELEASE command for a device.
    /// </summary>
    /// <param name="deviceId">The device ID to send the command to.</param>
    /// <param name="releaseId">The release ID to install.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous publish operation.</returns>
    Task PublishInstallReleaseAsync(Guid deviceId, Guid releaseId, CancellationToken cancellationToken = default);
}
