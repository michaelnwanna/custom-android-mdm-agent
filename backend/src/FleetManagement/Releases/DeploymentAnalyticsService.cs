namespace FleetManagement.Releases;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Service for calculating deployment analytics and progress metrics.
/// Provides real-time aggregation of device deployment states.
/// </summary>
public class DeploymentAnalyticsService
{
    private readonly IDbContextFactory<FleetDbContext> _dbContextFactory;

    public DeploymentAnalyticsService(IDbContextFactory<FleetDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    /// <summary>
    /// Get progress analytics for a single deployment.
    /// </summary>
    public async Task<DeploymentProgressAnalytics?> GetDeploymentProgressAsync(Guid deploymentId)
    {
        using var db = _dbContextFactory.CreateDbContext();

        // Get deployment info
        var deployment = await db.ReleaseDeployments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DeploymentId == deploymentId);

        if (deployment == null)
        {
            return null;
        }

        // Get status counts
        var counts = await db.DeploymentDeviceStates
            .AsNoTracking()
            .Where(s => s.DeploymentId == deploymentId)
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var statusCounts = new StatusCounts();
        foreach (var count in counts)
        {
            statusCounts = statusCounts with
            {
                Total = statusCounts.Total + count.Count
            };

            switch (count.Status)
            {
                case DeploymentDeviceStatus.Pending:
                    statusCounts = statusCounts with { Pending = count.Count };
                    break;
                case DeploymentDeviceStatus.Downloading:
                    statusCounts = statusCounts with { Downloading = count.Count };
                    break;
                case DeploymentDeviceStatus.Installing:
                    statusCounts = statusCounts with { Installing = count.Count };
                    break;
                case DeploymentDeviceStatus.Completed:
                    statusCounts = statusCounts with { Completed = count.Count };
                    break;
                case DeploymentDeviceStatus.Failed:
                    statusCounts = statusCounts with { Failed = count.Count };
                    break;
            }
        }

        // Get last updated timestamp
        var lastUpdate = await db.DeploymentDeviceStates
            .AsNoTracking()
            .Where(s => s.DeploymentId == deploymentId)
            .MaxAsync(s => (DateTimeOffset?)s.UpdatedAt);

        return new DeploymentProgressAnalytics(
            deploymentId: deploymentId,
            releaseId: deployment.ReleaseId,
            stage: deployment.Stage.ToString(),
            totalDevices: statusCounts.Total,
            counts: statusCounts,
            lastUpdatedAt: lastUpdate?.UtcDateTime
        );
    }

    /// <summary>
    /// Get progress analytics for all deployments in a release.
    /// Returns individual deployment analytics plus aggregated totals.
    /// </summary>
    public async Task<ReleaseProgressAnalytics?> GetReleaseProgressAsync(Guid releaseId)
    {
        using var db = _dbContextFactory.CreateDbContext();

        // Get release info
        var release = await db.Releases
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReleaseId == releaseId);

        if (release == null)
        {
            return null;
        }

        // Get all deployments for this release
        var deployments = await db.ReleaseDeployments
            .AsNoTracking()
            .Where(d => d.ReleaseId == releaseId)
            .Select(d => d.DeploymentId)
            .ToListAsync();

        if (deployments.Count == 0)
        {
            return new ReleaseProgressAnalytics(
                releaseId: releaseId,
                packageName: release.PackageName,
                versionCode: release.VersionCode,
                deployments: new List<DeploymentProgressAnalytics>(),
                totals: new StatusCounts()
            );
        }

        // Get analytics for each deployment
        var deploymentAnalytics = new List<DeploymentProgressAnalytics>();
        foreach (var deploymentId in deployments)
        {
            var analytics = await GetDeploymentProgressAsync(deploymentId);
            if (analytics != null)
            {
                deploymentAnalytics.Add(analytics);
            }
        }

        // Calculate totals
        var totalCounts = new StatusCounts
        {
            Total = (int)deploymentAnalytics.Sum(a => a.Counts.Total),
            Pending = (int)deploymentAnalytics.Sum(a => a.Counts.Pending),
            Downloading = (int)deploymentAnalytics.Sum(a => a.Counts.Downloading),
            Installing = (int)deploymentAnalytics.Sum(a => a.Counts.Installing),
            Completed = (int)deploymentAnalytics.Sum(a => a.Counts.Completed),
            Failed = (int)deploymentAnalytics.Sum(a => a.Counts.Failed)
        };

        return new ReleaseProgressAnalytics(
            releaseId: releaseId,
            packageName: release.PackageName,
            versionCode: release.VersionCode,
            deployments: deploymentAnalytics,
            totals: totalCounts
        );
    }

    /// <summary>
    /// Get status counts for multiple deployments.
    /// </summary>
    public async Task<Dictionary<Guid, StatusCounts>> GetDeploymentsProgressAsync(IEnumerable<Guid> deploymentIds)
    {
        var ids = deploymentIds.ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, StatusCounts>();
        }

        using var db = _dbContextFactory.CreateDbContext();

        // Get all device states for these deployments
        var states = await db.DeploymentDeviceStates
            .AsNoTracking()
            .Where(s => ids.Contains(s.DeploymentId))
            .GroupBy(s => new { s.DeploymentId, s.Status })
            .Select(g => new { g.Key.DeploymentId, Status = g.Key.Status, Count = g.Count() })
            .ToListAsync();

        // Organize by deployment
        var result = new Dictionary<Guid, StatusCounts>();
        foreach (var id in ids)
        {
            result[id] = new StatusCounts();
        }

        foreach (var state in states)
        {
            if (result.TryGetValue(state.DeploymentId, out var counts))
            {
                var updated = counts with { Total = counts.Total + state.Count };
                updated = state.Status switch
                {
                    DeploymentDeviceStatus.Pending => updated with { Pending = state.Count },
                    DeploymentDeviceStatus.Downloading => updated with { Downloading = state.Count },
                    DeploymentDeviceStatus.Installing => updated with { Installing = state.Count },
                    DeploymentDeviceStatus.Completed => updated with { Completed = state.Count },
                    DeploymentDeviceStatus.Failed => updated with { Failed = state.Count },
                    _ => updated
                };
                result[state.DeploymentId] = updated;
            }
        }

        return result;
    }
}

/// <summary>
/// Status count breakdown for a deployment.
/// </summary>
public record StatusCounts
{
    public int Total { get; init; }
    public int Pending { get; init; }
    public int Downloading { get; init; }
    public int Installing { get; init; }
    public int Completed { get; init; }
    public int Failed { get; init; }

    /// <summary>
    /// Completion percentage: (Completed + Failed) / Total, 0-100.
    /// Returns 0 if no devices assigned.
    /// </summary>
    public int CompletionPercentage => Total > 0
        ? (100 * (Completed + Failed)) / Total
        : 0;

    /// <summary>
    /// Success rate: Completed / Total, 0-100.
    /// Returns 0 if no devices assigned.
    /// </summary>
    public int SuccessRate => Total > 0
        ? (100 * Completed) / Total
        : 0;

    /// <summary>
    /// Failure rate: Failed / Total, 0-100.
    /// Returns 0 if no devices assigned.
    /// </summary>
    public int FailureRate => Total > 0
        ? (100 * Failed) / Total
        : 0;

    /// <summary>
    /// In-progress count: Downloading + Installing.
    /// </summary>
    public int InProgress => Downloading + Installing;
}

/// <summary>
/// Analytics for a single deployment.
/// </summary>
public record DeploymentProgressAnalytics
{
    public Guid DeploymentId { get; init; }
    public Guid ReleaseId { get; init; }
    public string Stage { get; init; }
    public int TotalDevices { get; init; }
    public StatusCounts Counts { get; init; }
    public DateTime? LastUpdatedAt { get; init; }

    public DeploymentProgressAnalytics(
        Guid deploymentId,
        Guid releaseId,
        string stage,
        int totalDevices,
        StatusCounts counts,
        DateTime? lastUpdatedAt)
    {
        DeploymentId = deploymentId;
        ReleaseId = releaseId;
        Stage = stage;
        TotalDevices = totalDevices;
        Counts = counts;
        LastUpdatedAt = lastUpdatedAt;
    }
}

/// <summary>
/// Analytics for all deployments in a release.
/// Includes per-deployment breakdown and aggregated totals.
/// </summary>
public record ReleaseProgressAnalytics
{
    public Guid ReleaseId { get; init; }
    public string PackageName { get; init; }
    public long VersionCode { get; init; }
    public List<DeploymentProgressAnalytics> Deployments { get; init; }
    public StatusCounts Totals { get; init; }

    public ReleaseProgressAnalytics(
        Guid releaseId,
        string packageName,
        long versionCode,
        List<DeploymentProgressAnalytics> deployments,
        StatusCounts totals)
    {
        ReleaseId = releaseId;
        PackageName = packageName;
        VersionCode = versionCode;
        Deployments = deployments ?? new List<DeploymentProgressAnalytics>();
        Totals = totals ?? new StatusCounts();
    }
}
