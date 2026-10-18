using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using Microsoft.EntityFrameworkCore;
using FleetManagement.Persistence;

namespace FleetManagement.Api.Mqtt;

/// <summary>
/// Hosted service that manages the MQTT broker connection and handles command publishing and event subscription.
/// Supports both legacy acknowledgements and new status reports from devices.
/// </summary>
public class MqttHostedService : BackgroundService, IMqttService, ICommandPublisher, IEventSubscriber
{
    private readonly ILogger<MqttHostedService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IDbContextFactory<FleetDbContext> _dbContextFactory;
    private IManagedMqttClient? _client;
    private readonly List<Func<DeviceStatusReport, Task>> _statusReportHandlers = [];
    private readonly List<Func<DeviceAcknowledgement, Task>> _acknowledgementHandlers = [];
    private readonly object _handlerLock = new();

    public MqttHostedService(
        ILogger<MqttHostedService> logger,
        IConfiguration configuration,
        IDbContextFactory<FleetDbContext> dbContextFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _dbContextFactory = dbContextFactory;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ConnectAsync(cancellationToken);
            await base.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start MQTT service");
            throw;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The service runs in the background. The main work happens in event handlers.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client != null)
        {
            await _client.StopAsync();
            _client.Dispose();
            _client = null;
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var brokerHost = _configuration["Mqtt:Broker:Host"] ?? "localhost";
        var brokerPort = _configuration.GetValue<int?>("Mqtt:Broker:Port") ?? 1883;

        var options = new ManagedMqttClientOptionsBuilder()
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
            .WithClientOptions(new MqttClientOptionsBuilder()
                .WithTcpServer(brokerHost, brokerPort)
                .WithClientId($"fleet-backend-{Guid.NewGuid():N}")
                .WithCleanSession(false)
                .Build())
            .Build();

        var factory = new MqttFactory();
        _client = factory.CreateManagedMqttClient();

        _client.ConnectedAsync += OnConnectedAsync;
        _client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;

        await _client.StartAsync(options);

        // Wait for initial connection
        int retries = 0;
        while (!_client.IsConnected && retries < 10)
        {
            await Task.Delay(100, cancellationToken);
            retries++;
        }

        if (!_client.IsConnected)
        {
            throw new InvalidOperationException("Failed to connect to MQTT broker");
        }

        _logger.LogInformation("Connected to MQTT broker at {Host}:{Port}", brokerHost, brokerPort);

        // Subscribe to acknowledgement topic pattern: fleet/devices/+/ack (legacy)
        var ackTopic = "fleet/devices/+/ack";
        // Subscribe to status report topic pattern: fleet/devices/+/status (new)
        var statusTopic = "fleet/devices/+/status";

        var subscriptions = new[] {
            new MqttTopicFilterBuilder()
                .WithTopic(ackTopic)
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                .Build(),
            new MqttTopicFilterBuilder()
                .WithTopic(statusTopic)
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                .Build()
        };
        await _client.SubscribeAsync(subscriptions);

        _logger.LogInformation("Subscribed to topics: {Topics}", string.Join(", ", ackTopic, statusTopic));
    }

    private Task OnConnectedAsync(MqttClientConnectedEventArgs arg)
    {
        _logger.LogInformation("MQTT client connected");
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs arg)
    {
        _logger.LogInformation("MQTT client disconnected. Will attempt to reconnect.");
        return Task.CompletedTask;
    }

    private async Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
    {
        try
        {
            var topic = arg.ApplicationMessage.Topic;
            var payload = arg.ApplicationMessage.PayloadSegment;
            var json = System.Text.Encoding.UTF8.GetString(payload);

            // Handle status reports: fleet/devices/{deviceId}/status
            if (topic.StartsWith("fleet/devices/") && topic.EndsWith("/status"))
            {
                var statusReport = JsonSerializer.Deserialize<DeviceStatusReport>(json);

                if (statusReport != null)
                {
                    _logger.LogInformation(
                        "Received status report from device {DeviceId}: status={Status}, progress={Progress}",
                        statusReport.DeviceId, statusReport.Status, statusReport.Progress);

                    // Call all registered status report handlers
                    List<Func<DeviceStatusReport, Task>> handlers;
                    lock (_handlerLock)
                    {
                        handlers = new List<Func<DeviceStatusReport, Task>>(_statusReportHandlers);
                    }

                    foreach (var handler in handlers)
                    {
                        try
                        {
                            await handler(statusReport);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error in status report handler");
                        }
                    }
                }
            }
            // Handle legacy acknowledgements: fleet/devices/{deviceId}/ack
            else if (topic.StartsWith("fleet/devices/") && topic.EndsWith("/ack"))
            {
                var ack = JsonSerializer.Deserialize<DeviceAcknowledgement>(json);

                if (ack != null)
                {
                    _logger.LogInformation(
                        "Received acknowledgement from device {DeviceId}: eventId={EventId}",
                        ack.DeviceId, ack.EventId);

                    // Call all registered acknowledgement handlers
                    List<Func<DeviceAcknowledgement, Task>> handlers;
                    lock (_handlerLock)
                    {
                        handlers = new List<Func<DeviceAcknowledgement, Task>>(_acknowledgementHandlers);
                    }

                    foreach (var handler in handlers)
                    {
                        try
                        {
                            await handler(ack);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error in acknowledgement handler");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MQTT message on topic {Topic}", arg.ApplicationMessage.Topic);
        }
    }

    public async Task PublishInstallReleaseAsync(Guid deviceId, Guid releaseId, CancellationToken cancellationToken = default)
    {
        if (_client == null || !_client.IsConnected)
        {
            throw new InvalidOperationException("MQTT client is not connected");
        }

        using var db = _dbContextFactory.CreateDbContext();

        // Get the release to ensure it exists
        var release = await db.Releases
            .FirstOrDefaultAsync(r => r.ReleaseId == releaseId, cancellationToken);

        if (release == null)
        {
            throw new ArgumentException($"Release {releaseId} not found");
        }

        // Create the command message
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
        var topic = $"fleet/devices/{deviceId:D}/command";

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(json)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(false)
            .Build();

        await _client.EnqueueAsync(message);

        _logger.LogInformation(
            "Published INSTALL_RELEASE command for device {DeviceId}, release {ReleaseId}",
            deviceId, releaseId);
    }

    public IDisposable OnStatusReport(Func<DeviceStatusReport, Task> handler)
    {
        lock (_handlerLock)
        {
            _statusReportHandlers.Add(handler);
        }

        return new StatusReportHandlerRemover(this, handler);
    }

    private void RemoveStatusReportHandler(Func<DeviceStatusReport, Task> handler)
    {
        lock (_handlerLock)
        {
            _statusReportHandlers.Remove(handler);
        }
    }

    public IDisposable OnAcknowledgement(Func<DeviceAcknowledgement, Task> handler)
    {
        lock (_handlerLock)
        {
            _acknowledgementHandlers.Add(handler);
        }

        return new HandlerRemover(this, handler);
    }

    private void RemoveHandler(Func<DeviceAcknowledgement, Task> handler)
    {
        lock (_handlerLock)
        {
            _acknowledgementHandlers.Remove(handler);
        }
    }

    private class StatusReportHandlerRemover : IDisposable
    {
        private readonly MqttHostedService _service;
        private readonly Func<DeviceStatusReport, Task> _handler;

        public StatusReportHandlerRemover(MqttHostedService service, Func<DeviceStatusReport, Task> handler)
        {
            _service = service;
            _handler = handler;
        }

        public void Dispose()
        {
            _service.RemoveStatusReportHandler(_handler);
        }
    }

    private class HandlerRemover : IDisposable
    {
        private readonly MqttHostedService _service;
        private readonly Func<DeviceAcknowledgement, Task> _handler;

        public HandlerRemover(MqttHostedService service, Func<DeviceAcknowledgement, Task> handler)
        {
            _service = service;
            _handler = handler;
        }

        public void Dispose()
        {
            _service.RemoveHandler(_handler);
        }
    }
}
