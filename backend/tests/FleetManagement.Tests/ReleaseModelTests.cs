namespace FleetManagement.Releases.Tests;

using System;
using System.Text.Json;
using Xunit;

/// <summary>
/// Unit tests for release domain models.
/// Tests idempotent ID generation, metadata validation, deployment stages, and device state tracking.
/// </summary>
public sealed class ReleaseModelTests
{
    private static readonly string ValidMetadataJson = @"{
  ""packageName"": ""com.example.app"",
  ""versionCode"": 42,
  ""apkSha256"": ""0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"",
  ""apkSizeBytes"": 1048576,
  ""minSdkLevel"": 29,
  ""expiresAt"": ""2099-12-31T23:59:59Z""
}";

    [Fact]
    public void Release_CreateFromMetadata_Generates_Idempotent_Id()
    {
        var metadata = ReleaseMetadata.FromJson(ValidMetadataJson);
        var now = DateTimeOffset.UtcNow;

        var release1 = Release.CreateFromMetadata(metadata, ValidMetadataJson, "sig1", now, "admin");
        var release2 = Release.CreateFromMetadata(metadata, ValidMetadataJson, "sig1", now, "admin");

        Assert.Equal(release1.ReleaseId, release2.ReleaseId);
    }

    [Fact]
    public void Release_CreateFromMetadata_Different_Metadata_Different_Id()
    {
        var metadata1 = ReleaseMetadata.FromJson(ValidMetadataJson);
        var metadata2Str = @"{
  ""packageName"": ""com.example.app"",
  ""versionCode"": 43,
  ""apkSha256"": ""0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"",
  ""apkSizeBytes"": 1048576,
  ""minSdkLevel"": 29,
  ""expiresAt"": ""2099-12-31T23:59:59Z""
}";
        var metadata2 = ReleaseMetadata.FromJson(metadata2Str);
        var now = DateTimeOffset.UtcNow;

        var release1 = Release.CreateFromMetadata(metadata1, ValidMetadataJson, "sig1", now, "admin");
        var release2 = Release.CreateFromMetadata(metadata2, metadata2Str, "sig1", now, "admin");

        Assert.NotEqual(release1.ReleaseId, release2.ReleaseId);
    }

    [Fact]
    public void Release_Stores_Metadata_And_Signature()
    {
        var metadata = ReleaseMetadata.FromJson(ValidMetadataJson);
        var now = DateTimeOffset.UtcNow;
        var sig = "test-signature-data";

        var release = Release.CreateFromMetadata(metadata, ValidMetadataJson, sig, now, "admin");

        Assert.Equal(ValidMetadataJson, release.MetadataJson);
        Assert.Equal(sig, release.MetadataSignature);
    }

    [Fact]
    public void ReleaseMetadata_FromJson_Valid_Metadata()
    {
        var metadata = ReleaseMetadata.FromJson(ValidMetadataJson);

        Assert.Equal("com.example.app", metadata.PackageName);
        Assert.Equal(42, metadata.VersionCode);
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", metadata.ApkSha256);
        Assert.Equal(1048576, metadata.ApkSizeBytes);
        Assert.Equal(29, metadata.MinSdkLevel);
    }

    [Fact]
    public void ReleaseMetadata_Validation_Rejects_Invalid_PackageName()
    {
        var invalidJson = @"{
  ""packageName"": ""invalid-package"",
  ""versionCode"": 42,
  ""apkSha256"": ""0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"",
  ""apkSizeBytes"": 1048576,
  ""minSdkLevel"": 29,
  ""expiresAt"": ""2099-12-31T23:59:59Z""
}";
        var metadata = ReleaseMetadata.FromJson(invalidJson);
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(
            () => Release.CreateFromMetadata(metadata, invalidJson, "sig", now, "admin"));
    }

    [Fact]
    public void ReleaseMetadata_Validation_Rejects_Invalid_Sha256()
    {
        var invalidJson = @"{
  ""packageName"": ""com.example.app"",
  ""versionCode"": 42,
  ""apkSha256"": ""tooshort"",
  ""apkSizeBytes"": 1048576,
  ""minSdkLevel"": 29,
  ""expiresAt"": ""2099-12-31T23:59:59Z""
}";
        var metadata = ReleaseMetadata.FromJson(invalidJson);
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(
            () => Release.CreateFromMetadata(metadata, invalidJson, "sig", now, "admin"));
    }

    [Fact]
    public void ReleaseMetadata_Validation_Rejects_Zero_VersionCode()
    {
        var invalidJson = @"{
  ""packageName"": ""com.example.app"",
  ""versionCode"": 0,
  ""apkSha256"": ""0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"",
  ""apkSizeBytes"": 1048576,
  ""minSdkLevel"": 29,
  ""expiresAt"": ""2099-12-31T23:59:59Z""
}";
        var metadata = ReleaseMetadata.FromJson(invalidJson);
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(
            () => Release.CreateFromMetadata(metadata, invalidJson, "sig", now, "admin"));
    }

    [Fact]
    public void ReleaseMetadata_Validation_Rejects_Oversized_Apk()
    {
        var invalidJson = @"{
  ""packageName"": ""com.example.app"",
  ""versionCode"": 42,
  ""apkSha256"": ""0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"",
  ""apkSizeBytes"": 1073741825,
  ""minSdkLevel"": 29,
  ""expiresAt"": ""2099-12-31T23:59:59Z""
}";
        var metadata = ReleaseMetadata.FromJson(invalidJson);
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(
            () => Release.CreateFromMetadata(metadata, invalidJson, "sig", now, "admin"));
    }

    [Fact]
    public void ReleaseDeployment_CreatePilot_Has_Correct_Stage()
    {
        var deploymentId = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var deployment = ReleaseDeployment.CreatePilot(releaseId, now, "admin");

        Assert.Equal(DeploymentStage.Pilot, deployment.Stage);
        Assert.True(deployment.IsActive);
        Assert.True(deployment.CreatedAt <= now);
    }

    [Fact]
    public void ReleaseDeployment_CreateStaged_Has_Correct_Stage()
    {
        var releaseId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var deployment = ReleaseDeployment.CreateStaged(releaseId, 25, now, "admin");

        Assert.Equal(DeploymentStage.Staged, deployment.Stage);
        Assert.Equal(25, deployment.RolloutPercentage);
        Assert.True(deployment.IsActive);
    }

    [Fact]
    public void ReleaseDeployment_CreateStaged_Validates_Rollout_Percentage()
    {
        var releaseId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() => ReleaseDeployment.CreateStaged(releaseId, 0, now, "admin"));
        Assert.Throws<ArgumentException>(() => ReleaseDeployment.CreateStaged(releaseId, 101, now, "admin"));
        Assert.Throws<ArgumentException>(() => ReleaseDeployment.CreateStaged(releaseId, -10, now, "admin"));
    }

    [Fact]
    public void ReleaseDeployment_Promote_Staged_To_Promoted()
    {
        var releaseId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var staged = ReleaseDeployment.CreateStaged(releaseId, 50, now, "admin");

        var promoted = staged.Promote();

        Assert.Equal(DeploymentStage.Promoted, promoted.Stage);
        Assert.Equal(100, promoted.RolloutPercentage);
        Assert.Equal(staged.DeploymentId, promoted.DeploymentId); // Same ID, different version
    }

    [Fact]
    public void ReleaseDeployment_PromoteInPlace_Staged_To_Promoted()
    {
        var releaseId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var staged = ReleaseDeployment.CreateStaged(releaseId, 50, now, "admin");

        staged.PromoteInPlace();

        Assert.Equal(DeploymentStage.Promoted, staged.Stage);
        Assert.Equal(100, staged.RolloutPercentage);
    }

    [Fact]
    public void ReleaseDeployment_Promote_Pilot_Throws()
    {
        var releaseId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var pilot = ReleaseDeployment.CreatePilot(releaseId, now, "admin");

        Assert.Throws<InvalidOperationException>(() => pilot.Promote());
    }

    [Fact]
    public void ReleaseDeployment_Promote_Already_Promoted_Throws()
    {
        var releaseId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var staged = ReleaseDeployment.CreateStaged(releaseId, 50, now, "admin");
        var promoted = staged.Promote();

        Assert.Throws<InvalidOperationException>(() => promoted.Promote());
    }

    [Fact]
    public void DeploymentDeviceState_CreatePending_Has_Correct_Status()
    {
        var deploymentId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var state = DeploymentDeviceState.CreatePending(deploymentId, enrollmentId, now);

        Assert.Equal(DeploymentDeviceStatus.Pending, state.Status);
        Assert.Null(state.ErrorMessage);
        Assert.Equal(deploymentId, state.DeploymentId);
        Assert.Equal(enrollmentId, state.EnrollmentId);
    }

    [Fact]
    public void DeploymentDeviceState_UpdateStatus_Transitions()
    {
        var deploymentId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var state = DeploymentDeviceState.CreatePending(deploymentId, enrollmentId, now);

        state.UpdateStatus(DeploymentDeviceStatus.Downloading, null, now.AddSeconds(1));
        Assert.Equal(DeploymentDeviceStatus.Downloading, state.Status);

        state.UpdateStatus(DeploymentDeviceStatus.Installing, null, now.AddSeconds(2));
        Assert.Equal(DeploymentDeviceStatus.Installing, state.Status);

        state.UpdateStatus(DeploymentDeviceStatus.Completed, null, now.AddSeconds(3));
        Assert.Equal(DeploymentDeviceStatus.Completed, state.Status);
    }

    [Fact]
    public void DeploymentDeviceState_UpdateStatus_Failed_Stores_Error()
    {
        var deploymentId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var state = DeploymentDeviceState.CreatePending(deploymentId, enrollmentId, now);

        var errorMsg = "Installation failed: out of disk space";
        state.UpdateStatus(DeploymentDeviceStatus.Failed, errorMsg, now.AddSeconds(1));

        Assert.Equal(DeploymentDeviceStatus.Failed, state.Status);
        Assert.Equal(errorMsg, state.ErrorMessage);
    }

    [Fact]
    public void Release_Tracks_Creator_And_Timestamps()
    {
        var metadata = ReleaseMetadata.FromJson(ValidMetadataJson);
        var now = DateTimeOffset.UtcNow;

        var release = Release.CreateFromMetadata(metadata, ValidMetadataJson, "sig", now, "alice@example.com");

        Assert.Equal("alice@example.com", release.CreatedBy);
        Assert.Equal(now, release.CreatedAt);
    }

    [Fact]
    public void ReleaseDeployment_Tracks_Creator_And_Timestamps()
    {
        var releaseId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var deployment = ReleaseDeployment.CreatePilot(releaseId, now, "bob@example.com");

        Assert.Equal("bob@example.com", deployment.CreatedBy);
        Assert.Equal(now, deployment.CreatedAt);
    }

    [Fact]
    public void DeploymentDeviceState_Tracks_Timestamps()
    {
        var deploymentId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var state = DeploymentDeviceState.CreatePending(deploymentId, enrollmentId, now);
        var later = now.AddSeconds(10);
        state.UpdateStatus(DeploymentDeviceStatus.Downloading, null, later);

        Assert.Equal(now, state.CreatedAt);
        Assert.Equal(later, state.UpdatedAt);
    }
}
