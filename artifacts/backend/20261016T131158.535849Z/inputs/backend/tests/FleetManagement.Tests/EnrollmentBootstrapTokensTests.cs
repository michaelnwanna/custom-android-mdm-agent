using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FleetManagement.Enrollment;
using Xunit;

namespace FleetManagement.Tests;

public sealed class EnrollmentBootstrapTokensTests
{
    private readonly ManualClock clock = new();
    private readonly byte[] digestKey = RandomNumberGenerator.GetBytes(32);
    private readonly Guid pendingId = Guid.NewGuid();

    [Fact]
    public void IssuedTokenHas256BitsOfEntropyAndOnlyKeyedDigestInCredential()
    {
        var service = new EnrollmentBootstrapTokens(clock);
        var issued = service.Issue(pendingId, digestKey, TimeSpan.FromMinutes(5));
        Assert.Equal(43, issued.Secret.Length);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", issued.Secret);
        Assert.Equal(32, Convert.FromBase64String(issued.Secret.Replace('-', '+').Replace('_', '/') + "=").Length);
        var expected = HMACSHA256.HashData(digestKey,
            Encoding.ASCII.GetBytes($"fleet/enrollment-token/v1:{pendingId:D}:{issued.Secret}"));
        Assert.Equal(Convert.ToHexStringLower(expected), issued.Credential.TokenDigest);
        Assert.Equal(clock.GetUtcNow(), issued.Credential.IssuedAt);
        Assert.Equal(clock.GetUtcNow().AddMinutes(5), issued.Credential.ExpiresAt);
        Assert.DoesNotContain(issued.Secret, JsonSerializer.Serialize(issued.Credential));
        Assert.True(service.Verify(pendingId, issued.Secret, issued.Credential, digestKey));
    }

    [Fact]
    public void SeparateIssuancesProduceDifferentSecretsAndDigests()
    {
        var service = new EnrollmentBootstrapTokens(clock);
        var first = service.Issue(pendingId, digestKey, TimeSpan.FromMinutes(1));
        var second = service.Issue(pendingId, digestKey, TimeSpan.FromMinutes(1));
        Assert.NotEqual(first.Secret, second.Secret);
        Assert.NotEqual(first.Credential.TokenDigest, second.Credential.TokenDigest);
        Assert.False(service.Verify(pendingId, first.Secret, second.Credential, digestKey));
    }

    [Fact]
    public void ExactDeadlineRejectsWithoutExtendingExpiry()
    {
        var service = new EnrollmentBootstrapTokens(clock);
        var issued = service.Issue(pendingId, digestKey, TimeSpan.FromMinutes(1));
        clock.Now = issued.Credential.ExpiresAt.AddTicks(-1);
        Assert.True(service.Verify(pendingId, issued.Secret, issued.Credential, digestKey));
        clock.Now = issued.Credential.ExpiresAt;
        Assert.False(service.Verify(pendingId, issued.Secret, issued.Credential, digestKey));
        clock.Now = clock.Now.AddMinutes(1);
        Assert.False(service.Verify(pendingId, issued.Secret, issued.Credential, digestKey));
        Assert.Equal(issued.Credential.IssuedAt.AddMinutes(1), issued.Credential.ExpiresAt);
    }

    [Fact]
    public void WrongEnrollmentIsRejectedEvenIfStoredIdentifierIsReplaced()
    {
        var service = new EnrollmentBootstrapTokens(clock);
        var issued = service.Issue(pendingId, digestKey, TimeSpan.FromMinutes(1));
        var otherId = Guid.NewGuid();
        Assert.False(service.Verify(otherId, issued.Secret, issued.Credential, digestKey));
        Assert.False(service.Verify(otherId, issued.Secret,
            issued.Credential with { PendingEnrollmentId = otherId }, digestKey));
    }

    [Fact]
    public void UnknownCredentialWrongKeyAndModifiedSecretAreRejected()
    {
        var service = new EnrollmentBootstrapTokens(clock);
        var issued = service.Issue(pendingId, digestKey, TimeSpan.FromMinutes(1));
        Assert.False(service.Verify(pendingId, issued.Secret, null, digestKey));
        Assert.False(service.Verify(pendingId, issued.Secret, issued.Credential, RandomNumberGenerator.GetBytes(32)));
        var changed = (issued.Secret[0] == 'A' ? "B" : "A") + issued.Secret[1..];
        Assert.False(service.Verify(pendingId, changed, issued.Credential, digestKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer secret")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ")]
    public void MissingMalformedOrNoncanonicalTokenIsRejected(string? secret)
    {
        var service = new EnrollmentBootstrapTokens(clock);
        var issued = service.Issue(pendingId, digestKey, TimeSpan.FromMinutes(1));
        Assert.False(service.Verify(pendingId, secret, issued.Credential, digestKey));
    }

    [Fact]
    public void CorruptStoredCredentialsFailClosed()
    {
        var service = new EnrollmentBootstrapTokens(clock);
        var issued = service.Issue(pendingId, digestKey, TimeSpan.FromMinutes(1));
        BootstrapTokenCredential[] invalid = [
            issued.Credential with { TokenDigest = new string('x', 64) },
            issued.Credential with { TokenDigest = "" },
            issued.Credential with { IssuedAt = clock.Now.AddSeconds(1) },
            issued.Credential with { ExpiresAt = issued.Credential.IssuedAt },
            issued.Credential with { ExpiresAt = clock.Now.AddMinutes(16) },
            issued.Credential with { PendingEnrollmentId = Guid.Empty }
        ];
        foreach (var credential in invalid)
            Assert.False(service.Verify(pendingId, issued.Secret, credential, digestKey));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(901)]
    public void InvalidLifetimeIsRejected(int seconds)
    {
        var service = new EnrollmentBootstrapTokens(clock);
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Issue(pendingId, digestKey, TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void MaximumLifetimeIsAcceptedAndEmptyPendingIdIsRejected()
    {
        var service = new EnrollmentBootstrapTokens(clock);
        var issued = service.Issue(pendingId, digestKey, EnrollmentBootstrapTokens.MaximumLifetime);
        Assert.True(service.Verify(pendingId, issued.Secret, issued.Credential, digestKey));
        Assert.Throws<ArgumentException>(() => service.Issue(Guid.Empty, digestKey, TimeSpan.FromMinutes(1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void WeakDigestKeyConfigurationIsRejected(int length)
    {
        var service = new EnrollmentBootstrapTokens(clock);
        var weakKey = new byte[length];
        Assert.Throws<ArgumentException>(() => service.Issue(pendingId, weakKey, TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentException>(() => service.Verify(pendingId, null, null, weakKey));
    }

    [Fact]
    public void DefaultDisplayAndJsonDoNotExposeTokenSecret()
    {
        var issued = new EnrollmentBootstrapTokens(clock).Issue(pendingId, digestKey, TimeSpan.FromMinutes(1));
        Assert.DoesNotContain(issued.Secret, issued.ToString());
        Assert.DoesNotContain(issued.Secret, JsonSerializer.Serialize(issued));
        Assert.Contains("REDACTED", issued.ToString());
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}