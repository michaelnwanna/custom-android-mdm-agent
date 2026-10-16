namespace FleetManagement.Enrollment;

public enum EnrollmentClaimStatus { Unauthorized, Claimed, InProgress, Conflict }

public sealed record EnrollmentClaim(EnrollmentClaimStatus Status, Guid? OperationId = null, Guid? DeviceId = null);

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

    internal EnrollmentClaim Claim(VerifiedEnrollmentKey key, DateTimeOffset now)
    {
        if (OperationId is not null)
            return new EnrollmentClaim(CsrSha256 == key.CsrSha256 ? EnrollmentClaimStatus.InProgress : EnrollmentClaimStatus.Conflict,
                CsrSha256 == key.CsrSha256 ? OperationId : null,
                CsrSha256 == key.CsrSha256 ? DeviceId : null);

        OperationId = Guid.NewGuid();
        DeviceId = Guid.NewGuid();
        CsrSha256 = key.CsrSha256;
        SubjectPublicKeyInfoBase64 = key.SubjectPublicKeyInfoBase64;
        ClaimedAt = now;
        return new EnrollmentClaim(EnrollmentClaimStatus.Claimed, OperationId, DeviceId);
    }

    private static bool IsDigest(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}