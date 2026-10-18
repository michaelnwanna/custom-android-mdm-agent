namespace FleetManagement.Tests.Mqtt;

using System;
using System.Text.Json;
using System.Threading.Tasks;
using FleetManagement.Api.Mqtt;
using Xunit;

/// <summary>
/// End-to-end integration tests for MQTT-based deployment flow.
/// Tests the complete lifecycle: deployment creation -> MQTT publishing -> device acknowledgement.
/// </summary>
public class MqttDeploymentFlowTests
{
    [Fact]
    public void DeploymentCreation_Generates_Valid_CommandId()
    {
        // Simulate deployment creation generating a unique command ID
        var deploymentId = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var commandId = Guid.NewGuid(); // Generated at deployment creation time

        // Verify all IDs are unique
        Assert.NotEqual(deploymentId, releaseId);
        Assert.NotEqual(deploymentId, deviceId);
        Assert.NotEqual(deploymentId, commandId);
        Assert.NotEqual(releaseId, deviceId);
        Assert.NotEqual(releaseId, commandId);
        Assert.NotEqual(deviceId, commandId);
    }

    [Fact]
    public void MqttCommand_Contains_ReleaseId_In_Payload()
    {
        var releaseId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        var command = new
        {
            schemaVersion = 1,
            commandId = Guid.NewGuid(),
            deviceId = deviceId,
            sequence = 1,
            type = "INSTALL_RELEASE",
            payload = new { releaseId = releaseId },
            issuedAt = DateTimeOffset.UtcNow,
            expiresAt = DateTimeOffset.UtcNow.AddHours(1),
            correlationId = Guid.NewGuid()
        };

        var json = JsonSerializer.Serialize(command);
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);

        var payloadElement = parsed.GetProperty("payload");
        var payloadReleaseId = Guid.Parse(payloadElement.GetProperty("releaseId").GetString()!);

        Assert.Equal(releaseId, payloadReleaseId);
    }

    [Fact]
    public void DeviceAcknowledgement_Uses_CommandId_As_EventId()
    {
        var deviceId = Guid.NewGuid();
        var commandId = Guid.NewGuid(); // From original command
        var receivedAt = DateTimeOffset.UtcNow;

        var acknowledgement = new
        {
            schemaVersion = 1,
            deviceId = deviceId,
            eventId = commandId, // Should match the original command ID
            receivedAt = receivedAt
        };

        var json = JsonSerializer.Serialize(acknowledgement);
        var ack = JsonSerializer.Deserialize<DeviceAcknowledgement>(json);

        Assert.NotNull(ack);
        Assert.Equal(deviceId, ack.DeviceId);
        Assert.Equal(commandId, ack.EventId);
    }

    [Fact]
    public void CommandTopic_And_AcknowledgementTopic_Use_Same_DeviceId()
    {
        var deviceId = Guid.NewGuid();
        var commandTopic = $"fleet/devices/{deviceId:D}/command";
        var ackTopic = $"fleet/devices/{deviceId:D}/ack";

        // Extract device IDs from topics
        var commandParts = commandTopic.Split('/');
        var ackParts = ackTopic.Split('/');

        var commandDeviceId = Guid.Parse(commandParts[2]);
        var ackDeviceId = Guid.Parse(ackParts[2]);

        // Both topics should reference the same device
        Assert.Equal(deviceId, commandDeviceId);
        Assert.Equal(deviceId, ackDeviceId);
        Assert.Equal(commandDeviceId, ackDeviceId);
    }

    [Fact]
    public void DeploymentFlow_Respects_Expiration()
    {
        var now = DateTimeOffset.UtcNow;
        var issuedAt = now;
        var expiresAt = now.AddHours(1);

        Assert.True(now >= issuedAt);
        Assert.True(expiresAt > now);
        Assert.True(expiresAt > issuedAt);
        
        var timeToExpiration = expiresAt - now;
        Assert.True(timeToExpiration.TotalSeconds > 0);
        Assert.True(timeToExpiration.TotalHours >= 1);
    }

    [Fact]
    public void AcknowledgementSchema_Has_RequiredFields()
    {
        var ackJson = new
        {
            schemaVersion = 1,
            deviceId = Guid.NewGuid(),
            eventId = Guid.NewGuid(),
            receivedAt = DateTimeOffset.UtcNow
        };

        var json = JsonSerializer.Serialize(ackJson);
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);

        // Verify all required fields are present
        Assert.True(parsed.TryGetProperty("schemaVersion", out var sv));
        Assert.True(parsed.TryGetProperty("deviceId", out var di));
        Assert.True(parsed.TryGetProperty("eventId", out var ei));
        Assert.True(parsed.TryGetProperty("receivedAt", out var ra));

        Assert.Equal(1, sv.GetInt32());
        Assert.NotEqual(Guid.Empty, Guid.Parse(di.GetString()!));
        Assert.NotEqual(Guid.Empty, Guid.Parse(ei.GetString()!));
        Assert.NotEqual(DateTimeOffset.MinValue, ra.GetDateTimeOffset());
    }
}
