using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FleetManagement.Enrollment;
using Xunit;

namespace FleetManagement.Tests;

public sealed class EnrollmentCertificateVerifierTests
{
    [Fact]
    public void AcceptsPinnedRootClientIdentityAndReturnsCertificateValidity()
    {
        using var fixture = new Certificates();
        var response = fixture.Verify();
        Assert.Equal(1, response.SchemaVersion);
        Assert.Equal(fixture.DeviceId, response.DeviceId);
        Assert.Equal(fixture.Encoded, response.CertificateDer);
        Assert.Empty(response.IntermediateCertificatesDer);
        Assert.Equal(fixture.Now.AddMinutes(-1), response.IssuedAt);
        Assert.Equal(fixture.Now.AddHours(1), response.ExpiresAt);
    }

    [Fact]
    public void RejectsOtherDeviceOtherKeyAndOtherTrustRoot()
    {
        using var fixture = new Certificates();
        using var other = new Certificates();
        Assert.Throws<InvalidEnrollmentCertificateException>(() => fixture.Verify(deviceId: Guid.NewGuid()));
        Assert.Throws<InvalidEnrollmentCertificateException>(() => fixture.Verify(publicKey: other.PublicKey));
        Assert.Throws<InvalidEnrollmentCertificateException>(() => fixture.Verify(root: other.Root));
    }

    [Fact]
    public void RejectsNotYetValidExactExpiryAndExcessiveLifetime()
    {
        using var fixture = new Certificates();
        Assert.Throws<InvalidEnrollmentCertificateException>(() => fixture.Verify(now: fixture.Now.AddMinutes(-2)));
        Assert.Throws<InvalidEnrollmentCertificateException>(() => fixture.Verify(now: fixture.Now.AddHours(1)));
        Assert.Throws<InvalidEnrollmentCertificateException>(() => fixture.Verify(maximum: TimeSpan.FromMinutes(30)));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    [InlineData("-----BEGIN CERTIFICATE-----")]
    public void RejectsMalformedCertificateWithoutEchoingInput(string encoded)
    {
        using var fixture = new Certificates();
        var error = Assert.Throws<InvalidEnrollmentCertificateException>(() => fixture.Verify(encoded: encoded));
        Assert.Equal("Invalid enrollment certificate.", error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void RejectsNoncanonicalTrailingAndOversizedCertificate()
    {
        using var fixture = new Certificates();
        Assert.Throws<InvalidEnrollmentCertificateException>(() => fixture.Verify(encoded: fixture.Encoded + "\n"));
        Assert.Throws<InvalidEnrollmentCertificateException>(() => fixture.Verify(encoded: new string('A', 16388)));
        var bytes = Convert.FromBase64String(fixture.Encoded).Concat(new byte[] { 0 }).ToArray();
        Assert.Throws<InvalidEnrollmentCertificateException>(() => fixture.Verify(encoded: Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void RejectsTrustRootInReturnedChainAndDuplicateIntermediates()
    {
        using var fixture = new Certificates();
        var root = Convert.ToBase64String(fixture.Root.RawData);
        Assert.Throws<InvalidEnrollmentCertificateException>(() => fixture.Verify(intermediates: [root]));
        Assert.Throws<InvalidEnrollmentCertificateException>(() => fixture.Verify(intermediates: [root, root]));
        Assert.Throws<InvalidEnrollmentCertificateException>(() => fixture.Verify(intermediates: [root, root, root, root, root]));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void RejectsCaServerUsageAndAdditionalDnsIdentity(bool ca, bool serverUsage, bool dns)
    {
        using var fixture = new Certificates(ca, serverUsage, dns);
        Assert.Throws<InvalidEnrollmentCertificateException>(() => fixture.Verify());
    }

    private sealed class Certificates : IDisposable
    {
        private readonly ECDsa rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public Guid DeviceId { get; } = Guid.NewGuid();
        public DateTimeOffset Now { get; } = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        public X509Certificate2 Root { get; }
        public string Encoded { get; }
        public string PublicKey => Convert.ToBase64String(deviceKey.ExportSubjectPublicKeyInfo());

        public Certificates(bool ca = false, bool serverUsage = false, bool dns = false)
        {
            var rootRequest = new CertificateRequest("CN=Fleet test root", rootKey, HashAlgorithmName.SHA256);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            Root = rootRequest.CreateSelfSigned(Now.AddDays(-1), Now.AddDays(7));
            var request = new CertificateRequest("CN=untrusted display", deviceKey, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new(serverUsage ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, true));
            var names = new SubjectAlternativeNameBuilder();
            names.AddUri(new Uri(EnrollmentCertificateVerifier.DeviceUri(DeviceId)));
            if (dns)
                names.AddDnsName("unexpected.example");
            request.CertificateExtensions.Add(names.Build());
            using var leaf = request.Create(Root, Now.AddMinutes(-1), Now.AddHours(1), RandomNumberGenerator.GetBytes(16));
            Encoded = Convert.ToBase64String(leaf.RawData);
        }

        public EnrollmentCertificateResponse Verify(Guid? deviceId = null, string? publicKey = null,
            X509Certificate2? root = null, DateTimeOffset? now = null, TimeSpan? maximum = null,
            string? encoded = null, IReadOnlyList<string>? intermediates = null) =>
            EnrollmentCertificateVerifier.Verify(deviceId ?? DeviceId, publicKey ?? PublicKey,
                encoded ?? Encoded, intermediates ?? [], root ?? Root, now ?? Now, maximum ?? TimeSpan.FromDays(1));

        public void Dispose()
        {
            Root.Dispose();
            deviceKey.Dispose();
            rootKey.Dispose();
        }
    }
}