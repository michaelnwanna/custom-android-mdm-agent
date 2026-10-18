using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FleetManagement.Releases;

/// <summary>
/// Represents a release of the managed application.
/// Immutable, versioned, with signed metadata for secure delivery.
/// </summary>
public sealed class Release
{
    /// <summary>
    /// Release ID (UUID derived from metadata hash for idempotency).
    /// </summary>
    public Guid ReleaseId { get; private set; }

    /// <summary>
    /// Package name (e.g., com.example.app).
    /// </summary>
    public string PackageName { get; private set; } = "";

    /// <summary>
    /// Version code (integer, must be monotonically increasing).
    /// </summary>
    public long VersionCode { get; private set; }

    /// <summary>
    /// APK SHA-256 hash (hex string, 64 chars).
    /// </summary>
    public string ApkSha256 { get; private set; } = "";

    /// <summary>
    /// APK size in bytes.
    /// </summary>
    public long ApkSizeBytes { get; private set; }

    /// <summary>
    /// Minimum Android SDK level required (e.g., 26 for Android 8.0).
    /// </summary>
    public int MinSdkLevel { get; private set; }

    /// <summary>
    /// Release expiry timestamp (UTC). After this, device will reject the release.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>
    /// Release metadata JSON (compact, used for signature verification).
    /// </summary>
    public string MetadataJson { get; private set; } = "";

    /// <summary>
    /// Release metadata signature (ECDP-256 SHA-256 or similar).
    /// </summary>
    public string MetadataSignature { get; private set; } = "";

    /// <summary>
    /// Whether the release is active (can be deployed to devices).
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// When the release was created (UTC).
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Creator subject (from OIDC token).
    /// </summary>
    public string CreatedBy { get; private set; } = "";

    private Release() { }

    /// <summary>
    /// Create a new release from signed metadata.
    /// Release ID is deterministically derived from metadata hash.
    /// </summary>
    public static Release CreateFromMetadata(
        ReleaseMetadata metadata,
        string metadataJson,
        string metadataSignature,
        DateTimeOffset createdAt,
        string creator)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrWhiteSpace(metadataJson))
            throw new ArgumentException("Metadata JSON required");
        if (string.IsNullOrWhiteSpace(metadataSignature))
            throw new ArgumentException("Metadata signature required");
        if (string.IsNullOrWhiteSpace(creator))
            throw new ArgumentException("Creator required");

        ValidateMetadata(metadata);

        // Derive release ID from metadata hash (idempotent)
        var releaseId = DeriveIdFromContent(metadataJson);

        return new Release
        {
            ReleaseId = releaseId,
            PackageName = metadata.PackageName,
            VersionCode = metadata.VersionCode,
            ApkSha256 = metadata.ApkSha256,
            ApkSizeBytes = metadata.ApkSizeBytes,
            MinSdkLevel = metadata.MinSdkLevel,
            ExpiresAt = metadata.ExpiresAt,
            MetadataJson = metadataJson,
            MetadataSignature = metadataSignature,
            IsActive = true,
            CreatedAt = createdAt,
            CreatedBy = creator
        };
    }

    private static void ValidateMetadata(ReleaseMetadata metadata)
    {
        // Package name validation (Android format)
        if (!System.Text.RegularExpressions.Regex.IsMatch(
            metadata.PackageName,
            @"^[a-zA-Z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)*$"))
            throw new ArgumentException("Invalid package name format");

        // Version code must be positive
        if (metadata.VersionCode <= 0)
            throw new ArgumentException("Version code must be positive");

        // SHA-256 hash: 64 hex characters
        if (!System.Text.RegularExpressions.Regex.IsMatch(metadata.ApkSha256, @"^[a-f0-9]{64}$"))
            throw new ArgumentException("Invalid SHA-256 hash format");

        // APK size must be positive
        if (metadata.ApkSizeBytes <= 0 || metadata.ApkSizeBytes > 1024 * 1024 * 1024) // Max 1 GB
            throw new ArgumentException("Invalid APK size (must be > 0 and <= 1 GB)");

        // Min SDK must be reasonable
        if (metadata.MinSdkLevel < 21 || metadata.MinSdkLevel > 99)
            throw new ArgumentException("Invalid minimum SDK level");

        // Expiry must be in the future
        if (metadata.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(-60))
            throw new ArgumentException("Release must not be expired");
    }

    private static Guid DeriveIdFromContent(string metadataJson)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(metadataJson);
        var hash = MD5.HashData(bytes);
        return new Guid(hash);
    }
}

/// <summary>
/// Immutable release metadata extracted from signed JSON.
/// </summary>
public sealed class ReleaseMetadata
{
    [JsonPropertyName("packageName")]
    public required string PackageName { get; init; }

    [JsonPropertyName("versionCode")]
    public required long VersionCode { get; init; }

    [JsonPropertyName("apkSha256")]
    public required string ApkSha256 { get; init; }

    [JsonPropertyName("apkSizeBytes")]
    public required long ApkSizeBytes { get; init; }

    [JsonPropertyName("minSdkLevel")]
    public required int MinSdkLevel { get; init; }

    [JsonPropertyName("expiresAt")]
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Deserialize metadata from JSON and validate structure.
    /// </summary>
    public static ReleaseMetadata FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };
            var metadata = JsonSerializer.Deserialize<ReleaseMetadata>(json, options);
            return metadata ?? throw new JsonException("Deserialized metadata is null");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid release metadata JSON: {ex.Message}", nameof(json), ex);
        }
    }
}

/// <summary>
/// Represents a deployment of a release to a set of devices.
/// Tracks pilot vs. production rollout and per-device progress.
/// </summary>
public sealed class ReleaseDeployment
{
    /// <summary>
    /// Deployment ID (UUID, unique per deployment).
    /// </summary>
    public Guid DeploymentId { get; private set; }

    /// <summary>
    /// Release being deployed.
    /// </summary>
    public Guid ReleaseId { get; private set; }

    /// <summary>
    /// Deployment stage: Pilot, Staged, or Promoted.
    /// </summary>
    public DeploymentStage Stage { get; private set; }

    /// <summary>
    /// Rollout percentage for Staged deployments (0-100).
    /// Ignored for Pilot and Promoted stages.
    /// </summary>
    public int RolloutPercentage { get; private set; }

    /// <summary>
    /// Whether the deployment is active (accepting new device assignments).
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// When the deployment was created (UTC).
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Creator subject (from OIDC token).
    /// </summary>
    public string CreatedBy { get; private set; } = "";

    /// <summary>
    /// Progress summary: total devices, pending, downloading, installing, completed, failed.
    /// Updated asynchronously as devices report state.
    /// </summary>
    public DeploymentProgress Progress { get; private set; } = new();

    private ReleaseDeployment() { }

    /// <summary>
    /// Create a new pilot deployment (limited scope, easy rollback).
    /// </summary>
    public static ReleaseDeployment CreatePilot(
        Guid releaseId,
        DateTimeOffset createdAt,
        string creator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(creator);
        if (releaseId == Guid.Empty)
            throw new ArgumentException("Release ID required");

        return new ReleaseDeployment
        {
            DeploymentId = Guid.NewGuid(),
            ReleaseId = releaseId,
            Stage = DeploymentStage.Pilot,
            RolloutPercentage = 100, // Pilots deploy to all selected devices
            IsActive = true,
            CreatedAt = createdAt,
            CreatedBy = creator
        };
    }

    /// <summary>
    /// Create a new staged deployment (percentage-based rollout).
    /// </summary>
    public static ReleaseDeployment CreateStaged(
        Guid releaseId,
        int rolloutPercentage,
        DateTimeOffset createdAt,
        string creator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(creator);
        if (releaseId == Guid.Empty)
            throw new ArgumentException("Release ID required");
        if (rolloutPercentage <= 0 || rolloutPercentage > 100)
            throw new ArgumentException("Rollout percentage must be 1-100");

        return new ReleaseDeployment
        {
            DeploymentId = Guid.NewGuid(),
            ReleaseId = releaseId,
            Stage = DeploymentStage.Staged,
            RolloutPercentage = rolloutPercentage,
            IsActive = true,
            CreatedAt = createdAt,
            CreatedBy = creator
        };
    }

    /// <summary>
    /// Promote a staged deployment to production (100% rollout).
    /// </summary>
    public ReleaseDeployment Promote()
    {
        if (Stage != DeploymentStage.Staged)
            throw new InvalidOperationException("Only staged deployments can be promoted");

        return new ReleaseDeployment
        {
            DeploymentId = DeploymentId,
            ReleaseId = ReleaseId,
            Stage = DeploymentStage.Promoted,
            RolloutPercentage = 100,
            IsActive = IsActive,
            CreatedAt = CreatedAt,
            CreatedBy = CreatedBy,
            Progress = Progress
        };
    }

    /// <summary>
    /// Promote this staged deployment to production (updates in place for EF tracking).
    /// </summary>
    public void PromoteInPlace()
    {
        if (Stage != DeploymentStage.Staged)
            throw new InvalidOperationException("Only staged deployments can be promoted");

        Stage = DeploymentStage.Promoted;
        RolloutPercentage = 100;
    }
}

/// <summary>
/// Deployment stages: Pilot (limited), Staged (percentage), Promoted (production).
/// </summary>
public enum DeploymentStage
{
    Pilot = 0,
    Staged = 1,
    Promoted = 2
}

/// <summary>
/// Tracks deployment progress across all devices.
/// </summary>
public sealed class DeploymentProgress
{
    /// <summary>
    /// Total devices targeted by deployment.
    /// </summary>
    public int TotalDevices { get; set; }

    /// <summary>
    /// Devices awaiting command or currently processing.
    /// </summary>
    public int PendingDevices { get; set; }

    /// <summary>
    /// Devices currently downloading APK.
    /// </summary>
    public int DownloadingDevices { get; set; }

    /// <summary>
    /// Devices currently installing APK.
    /// </summary>
    public int InstallingDevices { get; set; }

    /// <summary>
    /// Devices that successfully installed the release.
    /// </summary>
    public int CompletedDevices { get; set; }

    /// <summary>
    /// Devices that failed installation or reported errors.
    /// </summary>
    public int FailedDevices { get; set; }

    /// <summary>
    /// Last update timestamp (UTC).
    /// </summary>
    public DateTimeOffset LastUpdatedAt { get; set; }

    /// <summary>
    /// Completion percentage (0-100).
    /// </summary>
    public int CompletionPercentage =>
        TotalDevices == 0 ? 0 : (int)((CompletedDevices + FailedDevices) * 100 / TotalDevices);
}

/// <summary>
/// Tracks device-specific deployment state.
/// </summary>
public sealed class DeploymentDeviceState
{
    /// <summary>
    /// Unique state ID.
    /// </summary>
    public Guid StateId { get; private set; }

    /// <summary>
    /// Deployment this device is part of.
    /// </summary>
    public Guid DeploymentId { get; private set; }

    /// <summary>
    /// Enrolled device.
    /// </summary>
    public Guid EnrollmentId { get; private set; }

    /// <summary>
    /// Current device state: Pending, Downloading, Installing, Completed, Failed.
    /// </summary>
    public DeploymentDeviceStatus Status { get; private set; }

    /// <summary>
    /// For failed devices: error message.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// When the state was first created (UTC).
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// When the state was last updated (UTC).
    /// </summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    private DeploymentDeviceState() { }

    /// <summary>
    /// Create initial device deployment state (pending).
    /// </summary>
    public static DeploymentDeviceState CreatePending(
        Guid deploymentId,
        Guid enrollmentId,
        DateTimeOffset now)
    {
        if (deploymentId == Guid.Empty || enrollmentId == Guid.Empty)
            throw new ArgumentException("Deployment and enrollment IDs required");

        return new DeploymentDeviceState
        {
            StateId = Guid.NewGuid(),
            DeploymentId = deploymentId,
            EnrollmentId = enrollmentId,
            Status = DeploymentDeviceStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Update device state based on event from device.
    /// </summary>
    public void UpdateStatus(DeploymentDeviceStatus newStatus, string? errorMessage, DateTimeOffset now)
    {
        Status = newStatus;
        ErrorMessage = errorMessage;
        UpdatedAt = now;
    }
}

/// <summary>
/// Device-specific deployment status.
/// </summary>
public enum DeploymentDeviceStatus
{
    Pending = 0,
    Downloading = 1,
    Installing = 2,
    Completed = 3,
    Failed = 4
}
