using FleetManagement.Persistence;
using FleetManagement.Releases;
using Microsoft.EntityFrameworkCore;

namespace FleetManagement.Api.Mqtt;

/// <summary>
/// Background service that processes device acknowledgements from MQTT and updates device deployment state.
/// Maps acknowledgement events to device state transitions (Pending -> Downloading when acknowledged).
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
                "Received device acknowledgement: deviceId={DeviceId}, eventId={EventId}, receivedAt={ReceivedAt}",
                ack.DeviceId, ack.EventId, ack.ReceivedAt);

            // Create a scope to get database factory and release service
            using var scope = _serviceProvider.CreateScope();
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FleetDbContext>>();
            using var db = dbContextFactory.CreateDbContext();
            var releaseService = new ReleaseService(db);

            // Find device deployment state matching this device
            // We match on EnrollmentId (device ID) and look for the most recent deployment
            var stateRecord = await db.DeploymentDeviceStates
                .Where(s => s.EnrollmentId == ack.DeviceId)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            if (stateRecord == null)
            {
                _logger.LogWarning(
                    "No deployment found for device {DeviceId}",
                    ack.DeviceId);
                return;
            }

            // Transition from Pending to Downloading when device acknowledges receipt
            if (stateRecord.Status == DeploymentDeviceStatus.Pending)
            {
                var updated = await releaseService.UpdateDeviceDeploymentStateAsync(
                    stateRecord.DeploymentId,
                    stateRecord.EnrollmentId,
                    DeploymentDeviceStatus.Downloading
                );

                if (updated != null)
                {
                    _logger.LogInformation(
                        "Updated device {DeviceId} deployment state to Downloading",
                        ack.DeviceId);
                }
            }
            else
            {
                _logger.LogInformation(
                    "Device {DeviceId} acknowledged with status {CurrentStatus}, no state transition needed",
                    ack.DeviceId, stateRecord.Status);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing acknowledgement for device {DeviceId}", ack.DeviceId);
        }
    }
}
