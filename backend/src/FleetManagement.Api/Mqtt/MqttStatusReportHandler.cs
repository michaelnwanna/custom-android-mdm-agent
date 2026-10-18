using FleetManagement.Persistence;
using FleetManagement.Releases;
using Microsoft.EntityFrameworkCore;

namespace FleetManagement.Api.Mqtt;

/// <summary>
/// Background service that processes device status reports from MQTT and updates device deployment state.
/// Implements full state machine: Acknowledged → Downloading (with progress) → Installing → Installed/Failed
/// </summary>
public class MqttStatusReportHandler : BackgroundService
{
    private readonly ILogger<MqttStatusReportHandler> _logger;
    private readonly IServiceProvider _serviceProvider;
    private IDisposable? _subscription;

    public MqttStatusReportHandler(
        ILogger<MqttStatusReportHandler> logger,
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
            _subscription = subscriber.OnStatusReport(OnStatusReportReceivedAsync);
            _logger.LogInformation("MQTT status report handler started");
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

    private async Task OnStatusReportReceivedAsync(DeviceStatusReport report)
    {
        try
        {
            _logger.LogInformation(
                "Received status report from device {DeviceId}: status={Status}, progress={Progress}, error={Error}",
                report.DeviceId, report.Status, report.Progress, report.ErrorMessage);

            // Create a scope to get database factory and release service
            using var scope = _serviceProvider.CreateScope();
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FleetDbContext>>();
            using var db = dbContextFactory.CreateDbContext();
            var releaseService = new ReleaseService(db);

            // Find device deployment state matching this device
            // For now, match on most recent deployment (enhancement: use CommandId from report)
            var stateRecord = await db.DeploymentDeviceStates
                .Where(s => s.EnrollmentId == report.DeviceId)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            if (stateRecord == null)
            {
                _logger.LogWarning(
                    "No deployment found for device {DeviceId}",
                    report.DeviceId);
                return;
            }

            // Map MQTT status to deployment device status
            var newStatus = MapMqttStatusToDeviceStatus(report.Status);
            if (newStatus == null)
            {
                _logger.LogWarning(
                    "Unknown device status '{Status}' for device {DeviceId}",
                    report.Status, report.DeviceId);
                return;
            }

            // Update the device state with status and error message
            var updated = await releaseService.UpdateDeviceDeploymentStateAsync(
                stateRecord.DeploymentId,
                stateRecord.EnrollmentId,
                newStatus.Value,
                report.ErrorMessage
            );

            if (updated != null)
            {
                _logger.LogInformation(
                    "Updated device {DeviceId} deployment state to {Status}",
                    report.DeviceId, newStatus);

                // Log progress for downloading status
                if (report.Status == MqttDeviceStatus.Downloading && report.Progress.HasValue)
                {
                    _logger.LogInformation(
                        "Device {DeviceId} download progress: {Progress}%",
                        report.DeviceId, report.Progress);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing status report for device {DeviceId}", report.DeviceId);
        }
    }

    private static DeploymentDeviceStatus? MapMqttStatusToDeviceStatus(MqttDeviceStatus mqttStatus)
    {
        return mqttStatus switch
        {
            MqttDeviceStatus.Acknowledged => DeploymentDeviceStatus.Downloading,
            MqttDeviceStatus.Downloading => DeploymentDeviceStatus.Downloading,
            MqttDeviceStatus.Downloaded => DeploymentDeviceStatus.Installing,
            MqttDeviceStatus.Installing => DeploymentDeviceStatus.Installing,
            MqttDeviceStatus.Installed => DeploymentDeviceStatus.Completed,
            MqttDeviceStatus.Failed => DeploymentDeviceStatus.Failed,
            _ => null
        };
    }
}
