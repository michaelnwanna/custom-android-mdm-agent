using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FleetManagement.Api.Mqtt;

/// <summary>
/// Background service that processes device acknowledgements from MQTT and logs them.
/// Future enhancement: Update device deployment state when proper state tracking is implemented.
/// </summary>
public class MqttAcknowledgementHandler : BackgroundService
{
    private readonly ILogger<MqttAcknowledgementHandler> _logger;
    private readonly IServiceProvider _serviceProvider;
    private IDisposable? _subscription;

    public MqttAcknowledgementHandler(
        ILogger<MqttAcknowledgementHandler> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var subscriber = scope.ServiceProvider.GetService<IEventSubscriber>();

        if (subscriber != null)
        {
            _subscription = subscriber.OnAcknowledgement(OnAcknowledgementReceivedAsync);
            _logger.LogInformation("MQTT acknowledgement handler started");
        }

        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The service runs in the background. The main work happens in event handlers.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task OnAcknowledgementReceivedAsync(DeviceAcknowledgement ack)
    {
        try
        {
            _logger.LogInformation(
                "Received device acknowledgement: deviceId={DeviceId}, commandId={CommandId}, receivedAt={ReceivedAt}",
                ack.DeviceId, ack.EventId, ack.ReceivedAt);

            // Future enhancement: Update device deployment state in database
            // For now, just log the event
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing acknowledgement for device {DeviceId}", ack.DeviceId);
        }
    }
}
