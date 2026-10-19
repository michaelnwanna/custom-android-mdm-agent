using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FleetManagement.Tests;

public sealed class EnrollmentPersistenceTests
{
    [Fact]
    public void InitialMigrationMatchesModelAndGeneratesPostgresSql()
    {
        using var context = new FleetDbContext(new DbContextOptionsBuilder<FleetDbContext>()
            .UseNpgsql("Host=localhost;Database=not_connected;Username=unused").Options);
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(10, context.Database.GetMigrations().Count());
        var sql = context.GetService<IMigrator>().GenerateScript();
        Assert.Contains("CREATE TABLE \"PendingEnrollments\"", sql);
        Assert.Contains("CREATE TABLE \"CommandEventOwnerships\"", sql);
        Assert.Contains("CREATE TABLE \"StoredCommandEvents\"", sql);
        Assert.Contains("FOREIGN KEY (\"CommandId\", \"DeviceId\", \"CorrelationId\")", sql);
        Assert.Contains("CK_Enrollment_Claim", sql);
        Assert.Contains("__EFMigrationsHistory", sql);
    }

    [Fact]
    public void NpgsqlModelProducesBoundedColumnsUniqueIdentityAndClaimConstraints()
    {
        using var context = new FleetDbContext(new DbContextOptionsBuilder<FleetDbContext>()
            .UseNpgsql("Host=localhost;Database=not_connected;Username=unused").Options);
        var sql = context.Database.GenerateCreateScript();
        Assert.Contains("CREATE TABLE \"PendingEnrollments\"", sql);
        Assert.Contains("CK_Enrollment_Claim", sql);
        Assert.Contains("CK_Enrollment_Lifetime", sql);
        Assert.Contains("CK_Enrollment_Digest", sql);
        Assert.Contains("CK_Enrollment_Completion", sql);
        Assert.Contains("CK_Enrollment_SigningAttempt", sql);
        Assert.Contains("\"DeviceAccessDisabled\" boolean NOT NULL DEFAULT FALSE", sql);
        Assert.Contains("character varying(100000)", sql);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_PendingEnrollments_OperationId\"", sql);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_PendingEnrollments_DeviceId\"", sql);
        Assert.Contains("character varying(512)", sql);
        Assert.DoesNotContain("Secret", sql);
        Assert.DoesNotContain("Credential", sql);
    }

    [Fact]
    public void SameCsrRetainsClaimIdentityAndDifferentCsrConflictsWithoutMutation()
    {
        var issued = Issue();
        var pending = PendingEnrollment.Create(issued.Credential);
        var key = CreateVerifiedKey();
        var first = pending.Claim(key, issued.Credential.IssuedAt);
        Assert.Equal(EnrollmentClaimStatus.Claimed, first.Status);
        Assert.NotNull(first.DeviceId);
        Assert.NotNull(first.OperationId);
        Assert.NotEqual(Guid.Empty, first.DeviceId);
        Assert.NotEqual(Guid.Empty, first.OperationId);
        var retry = pending.Claim(key, issued.Credential.IssuedAt.AddSeconds(1));
        Assert.Equal(EnrollmentClaimStatus.InProgress, retry.Status);
        Assert.Equal(first.OperationId, retry.OperationId);
        Assert.Equal(first.DeviceId, retry.DeviceId);
        var conflict = pending.Claim(CreateVerifiedKey(), issued.Credential.IssuedAt.AddSeconds(2));
        Assert.Equal(EnrollmentClaimStatus.Conflict, conflict.Status);
        Assert.Null(conflict.OperationId);
        Assert.Null(conflict.DeviceId);
        Assert.Equal(first.OperationId, pending.OperationId);
        Assert.Equal(first.DeviceId, pending.DeviceId);
        Assert.Equal(key.CsrSha256, pending.CsrSha256);
        Assert.Equal(key.SubjectPublicKeyInfoBase64, pending.SubjectPublicKeyInfoBase64);
        Assert.Equal(issued.Credential.IssuedAt, pending.ClaimedAt);
        Assert.Equal(issued.Credential, pending.Credential);
    }

    [Fact]
    public void InvalidCredentialsCannotBecomePendingRows()
    {
        var credential = Issue().Credential;
        BootstrapTokenCredential[] invalid = [
            credential with { PendingEnrollmentId = Guid.Empty },
            credential with { TokenDigest = "bad" },
            credential with { ExpiresAt = credential.IssuedAt },
            credential with { ExpiresAt = credential.IssuedAt.AddMinutes(16) },
            credential with { IssuedAt = credential.IssuedAt.ToOffset(TimeSpan.FromHours(1)) }
        ];
        foreach (var candidate in invalid)
            Assert.Throws<ArgumentException>(() => PendingEnrollment.Create(candidate));
    }

    [Fact]
    public void CompletionIsBoundToClaimAndFirstResponseCannotBeReplaced()
    {
        var credential = Issue().Credential;
        var pending = PendingEnrollment.Create(credential);
        Assert.False(pending.DeviceAccessDisabled);
        Assert.Throws<InvalidOperationException>(() => pending.DisableDeviceAccess());
        var key = CreateVerifiedKey();
        var claim = pending.Claim(key, credential.IssuedAt);
        var response = new EnrollmentCertificateResponse(1, claim.DeviceId!.Value, "synthetic-component-only",
            Array.AsReadOnly(Array.Empty<string>()), credential.IssuedAt, credential.IssuedAt.AddDays(1));
        Assert.Throws<InvalidOperationException>(() => pending.Complete(Guid.NewGuid(), response));
        Assert.Null(pending.CompletedResponseJson);
        Assert.Throws<InvalidOperationException>(() => pending.Complete(claim.OperationId!.Value,
            response with { DeviceId = Guid.NewGuid() }));
        pending.Complete(claim.OperationId!.Value, response);
        var stored = pending.CompletedResponseJson;
        var retry = pending.Complete(claim.OperationId.Value, response with { CertificateDer = "replacement" });
        Assert.Equal(response.CertificateDer, retry.CertificateDer);
        Assert.Equal(stored, pending.CompletedResponseJson);
        var recovered = pending.Claim(key, credential.IssuedAt.AddSeconds(1));
        Assert.Equal(EnrollmentClaimStatus.Completed, recovered.Status);
        Assert.Equal(response.CertificateDer, recovered.Response!.CertificateDer);
        Assert.Equal(claim.DeviceId, recovered.Response.DeviceId);
        var conflict = pending.Claim(CreateVerifiedKey(), credential.IssuedAt.AddSeconds(2));
        Assert.Equal(EnrollmentClaimStatus.Conflict, conflict.Status);
        Assert.Null(conflict.Response);
        pending.DisableDeviceAccess();
        pending.DisableDeviceAccess();
        Assert.True(pending.DeviceAccessDisabled);
        Assert.Equal(stored, pending.CompletedResponseJson);
    }

    [Fact]
    public void SigningAttemptCanStartOnlyOnceForTheCommittedOperation()
    {
        var credential = Issue().Credential;
        var pending = PendingEnrollment.Create(credential);
        Assert.Throws<InvalidOperationException>(() => pending.TryBeginSigning(Guid.NewGuid()));
        var claim = pending.Claim(CreateVerifiedKey(), credential.IssuedAt);
        Assert.Throws<InvalidOperationException>(() => pending.TryBeginSigning(Guid.NewGuid()));
        Assert.False(pending.SigningAttempted);
        Assert.True(pending.TryBeginSigning(claim.OperationId!.Value));
        Assert.True(pending.SigningAttempted);
        Assert.False(pending.TryBeginSigning(claim.OperationId.Value));
        Assert.Equal(claim.OperationId, pending.OperationId);
    }

    private static IssuedBootstrapToken Issue() => new EnrollmentBootstrapTokens(TimeProvider.System)
        .Issue(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32), TimeSpan.FromMinutes(5));

    private static VerifiedEnrollmentKey CreateVerifiedKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return EnrollmentCsrVerifier.Verify(Convert.ToBase64String(
            new CertificateRequest("CN=untrusted", key, HashAlgorithmName.SHA256).CreateSigningRequest()));
    }
}