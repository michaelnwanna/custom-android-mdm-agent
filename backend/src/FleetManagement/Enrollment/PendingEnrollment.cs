using System.Text.Json;

namespace FleetManagement.Enrollment;

public enum EnrollmentClaimStatus { Unauthorized, Claimed, InProgress, Conflict, Completed }

public sealed record EnrollmentClaim(EnrollmentClaimStatus Status, Guid? OperationId = null, Guid? DeviceId = null,
    EnrollmentCertificateResponse? Response = null);

public sealed class PendingEnrollment
{
    public Guid Id { get; private set; }
    public string TokenDigest { get; private set; } = "";
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public Guid? OperationId { get; private set; }
    public Guid? DeviceId { get; private set; }
    public string? CsrSha256 { get; private set; }
    public string? SubjectPublicKeyInfoBase64 { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public string? CompletedResponseJson { get; private set; }
    public bool SigningAttempted { get; private set; }
    public bool DeviceAccessDisabled { get; private set; }

    private PendingEnrollment() { }

    public static PendingEnrollment Create(BootstrapTokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (credential.PendingEnrollmentId == Guid.Empty || !IsDigest(credential.TokenDigest)
            || credential.IssuedAt.Offset != TimeSpan.Zero || credential.ExpiresAt.Offset != TimeSpan.Zero
            || credential.ExpiresAt <= credential.IssuedAt
            || credential.ExpiresAt - credential.IssuedAt > EnrollmentBootstrapTokens.MaximumLifetime)
            throw new ArgumentException("Invalid bootstrap credential.", nameof(credential));
        return new PendingEnrollment
        {
            Id = credential.PendingEnrollmentId, TokenDigest = credential.TokenDigest,
            IssuedAt = credential.IssuedAt, ExpiresAt = credential.ExpiresAt
        };
    }

    public BootstrapTokenCredential Credential => new(Id, TokenDigest, IssuedAt, ExpiresAt);

    public void DisableDeviceAccess()
    {
        if (CompletedResponseJson is null)
            throw new InvalidOperationException("Device enrollment is not complete.");
        DeviceAccessDisabled = true;
    }

    internal EnrollmentClaim Claim(VerifiedEnrollmentKey key, DateTimeOffset now)
    {
        if (OperationId is not null)
            return new EnrollmentClaim(CsrSha256 == key.CsrSha256
                    ? CompletedResponseJson is null ? EnrollmentClaimStatus.InProgress : EnrollmentClaimStatus.Completed
                    : EnrollmentClaimStatus.Conflict,
                CsrSha256 == key.CsrSha256 ? OperationId : null,
                CsrSha256 == key.CsrSha256 ? DeviceId : null,
                CsrSha256 == key.CsrSha256 ? CompletedResponse : null);

        OperationId = Guid.NewGuid();
        DeviceId = Guid.NewGuid();
        CsrSha256 = key.CsrSha256;
        SubjectPublicKeyInfoBase64 = key.SubjectPublicKeyInfoBase64;
        ClaimedAt = now;
        return new EnrollmentClaim(EnrollmentClaimStatus.Claimed, OperationId, DeviceId);
    }

    internal EnrollmentCertificateResponse? CompletedResponse => CompletedResponseJson is null ? null
        : JsonSerializer.Deserialize<EnrollmentCertificateResponse>(CompletedResponseJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Invalid persisted enrollment completion.");

    internal EnrollmentCertificateResponse Complete(Guid operationId, EnrollmentCertificateResponse response)
    {
        if (OperationId is null || operationId != OperationId || response.DeviceId != DeviceId)
            throw new InvalidOperationException("Enrollment operation does not match its claim.");
        if (CompletedResponse is { } existing)
            return existing;
        CompletedResponseJson = JsonSerializer.Serialize(response, JsonSerializerOptions.Web);
        return CompletedResponse!;
    }

    internal bool TryBeginSigning(Guid operationId)
    {
        if (OperationId is null || OperationId != operationId)
            throw new InvalidOperationException("Enrollment operation does not match its claim.");
        if (SigningAttempted || CompletedResponseJson is not null)
            return false;
        SigningAttempted = true;
        return true;
    }

    private static bool IsDigest(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}