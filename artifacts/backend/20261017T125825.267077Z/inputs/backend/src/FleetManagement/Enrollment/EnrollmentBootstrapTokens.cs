using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace FleetManagement.Enrollment;

public sealed record BootstrapTokenCredential(
    Guid PendingEnrollmentId, string TokenDigest, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

public sealed class IssuedBootstrapToken
{
    [JsonIgnore]
    public string Secret { get; }
    public BootstrapTokenCredential Credential { get; }

    internal IssuedBootstrapToken(string secret, BootstrapTokenCredential credential)
    {
        Secret = secret;
        Credential = credential;
    }

    public override string ToString() => "IssuedBootstrapToken { Secret = [REDACTED] }";
}

public sealed class EnrollmentBootstrapTokens(TimeProvider clock)
{
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private readonly TimeProvider clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public IssuedBootstrapToken Issue(Guid pendingEnrollmentId, ReadOnlySpan<byte> digestKey, TimeSpan lifetime)
    {
        ValidateKey(digestKey);
        if (pendingEnrollmentId == Guid.Empty)
            throw new ArgumentException("A pending enrollment identifier is required.", nameof(pendingEnrollmentId));
        if (lifetime <= TimeSpan.Zero || lifetime > MaximumLifetime)
            throw new ArgumentOutOfRangeException(nameof(lifetime));

        var entropy = RandomNumberGenerator.GetBytes(32);
        try
        {
            var secret = Encode(entropy);
            var issuedAt = clock.GetUtcNow();
            var digest = ComputeDigest(pendingEnrollmentId, secret, digestKey);
            return new IssuedBootstrapToken(secret, new BootstrapTokenCredential(
                pendingEnrollmentId, Convert.ToHexStringLower(digest), issuedAt, issuedAt.Add(lifetime)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    public bool Verify(Guid pendingEnrollmentId, string? secret, BootstrapTokenCredential? credential,
        ReadOnlySpan<byte> digestKey)
    {
        ValidateKey(digestKey);
        if (secret is null || secret.Length != 43)
            return false;

        byte[] entropy;
        try
        {
            entropy = Convert.FromBase64String(secret.Replace('-', '+').Replace('_', '/') + "=");
        }
        catch (FormatException)
        {
            return false;
        }
        try
        {
            if (entropy.Length != 32 || !string.Equals(Encode(entropy), secret, StringComparison.Ordinal))
                return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropy);
        }

        var candidate = ComputeDigest(pendingEnrollmentId, secret, digestKey);
        var expected = new byte[32];
        var validDigest = false;
        if (credential?.TokenDigest is { Length: 64 } storedDigest)
        {
            try
            {
                expected = Convert.FromHexString(storedDigest);
                validDigest = expected.Length == 32;
            }
            catch (FormatException)
            {
                validDigest = false;
            }
        }
        var matches = CryptographicOperations.FixedTimeEquals(candidate, expected);
        var now = clock.GetUtcNow();
        return matches && validDigest && pendingEnrollmentId != Guid.Empty && credential is not null
            && credential.PendingEnrollmentId == pendingEnrollmentId
            && credential.IssuedAt <= now && now < credential.ExpiresAt
            && credential.ExpiresAt > credential.IssuedAt
            && credential.ExpiresAt - credential.IssuedAt <= MaximumLifetime;
    }

    private static byte[] ComputeDigest(Guid pendingEnrollmentId, string secret, ReadOnlySpan<byte> digestKey)
    {
        var material = Encoding.ASCII.GetBytes($"fleet/enrollment-token/v1:{pendingEnrollmentId:D}:{secret}");
        try
        {
            return HMACSHA256.HashData(digestKey, material);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    private static string Encode(byte[] entropy) =>
        Convert.ToBase64String(entropy).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void ValidateKey(ReadOnlySpan<byte> digestKey)
    {
        if (digestKey.Length < 32)
            throw new ArgumentException("The digest key must contain at least 32 bytes.", nameof(digestKey));
    }
}