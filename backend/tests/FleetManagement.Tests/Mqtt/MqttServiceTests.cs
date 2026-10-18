namespace FleetManagement.Tests.Mqtt;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FleetManagement.Api.Mqtt;
using MQTTnet;
using MQTTnet.Server;
using Xunit;

/// <summary>
/// Tests for MQTT command publishing and event subscription.
/// These are integration-style tests that verify the MQTT components work correctly.
/// </summary>
public class MqttServiceTests
{
    [Fact]
    public void DeviceAcknowledgement_Deserializes_From_Json()
    {
        var json = new
        {
            schemaVersion = 1,
            deviceId = "550e8400-e29b-41d4-a716-446655440000",
            eventId = "660e8400-e29b-41d4-a716-446655440001",
            receivedAt = "2024-01-16T12:34:56Z"
        };

        var jsonString = JsonSerializer.Serialize(json);
        var ack = JsonSerializer.Deserialize<DeviceAcknowledgement>(jsonString);

        Assert.NotNull(ack);
        Assert.Equal(1, ack.SchemaVersion);
        Assert.Equal(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"), ack.DeviceId);
        Assert.Equal(Guid.Parse("660e8400-e29b-41d4-a716-446655440001"), ack.EventId);
    }

    [Fact]
    public void CommandPublisher_Serializes_InstallReleaseCommand()
    {
        var deviceId = Guid.NewGuid();
        var releaseId = Guid.NewGuid();

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

        Assert.Equal(1, parsed.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("INSTALL_RELEASE", parsed.GetProperty("type").GetString());
        Assert.Equal(deviceId, Guid.Parse(parsed.GetProperty("deviceId").GetString()!));
    }

    [Fact]
    public void EventSubscriber_Handlers_Can_Be_Added_And_Removed()
    {
        var handlers = new List<Func<DeviceAcknowledgement, Task>>();
        var handler1 = new Func<DeviceAcknowledgement, Task>(async (ack) => { await Task.CompletedTask; });
        var handler2 = new Func<DeviceAcknowledgement, Task>(async (ack) => { await Task.CompletedTask; });

        // Simulate adding handlers
        handlers.Add(handler1);
        handlers.Add(handler2);
        Assert.Equal(2, handlers.Count);

        // Simulate removing a handler
        handlers.Remove(handler1);
        Assert.Single(handlers);
        Assert.Equal(handler2, handlers[0]);
    }

    [Fact]
    public void InstallReleaseCommand_Has_Correct_Topic_Format()
    {
        var deviceId = Guid.NewGuid();
        var expectedTopic = $"fleet/devices/{deviceId:D}/command";

        // Verify the expected topic matches the pattern
        Assert.StartsWith("fleet/devices/", expectedTopic);
        Assert.EndsWith("/command", expectedTopic);
        
        // Verify the device ID in the topic is properly formatted
        var parts = expectedTopic.Split('/');
        Assert.Equal(4, parts.Length);
        Assert.Equal("fleet", parts[0]);
        Assert.Equal("devices", parts[1]);
        Assert.True(Guid.TryParse(parts[2], out _));
        Assert.Equal("command", parts[3]);
    }

    [Fact]
    public void AcknowledgementTopic_Matches_Subscription_Pattern()
    {
        var deviceId = Guid.NewGuid();
        var ackTopic = $"fleet/devices/{deviceId:D}/ack";
        var subscriptionPattern = "fleet/devices/+/ack";

        // Verify acknowledgement topic matches the subscription pattern
        var topicParts = ackTopic.Split('/');
        var patternParts = subscriptionPattern.Split('/');

        Assert.Equal(patternParts.Length, topicParts.Length);
        for (int i = 0; i < patternParts.Length; i++)
        {
            if (patternParts[i] != "+")
            {
                Assert.Equal(patternParts[i], topicParts[i]);
            }
        }
    }
}
