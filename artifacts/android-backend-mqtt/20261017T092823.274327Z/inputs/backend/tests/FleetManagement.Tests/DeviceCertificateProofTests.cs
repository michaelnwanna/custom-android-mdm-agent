using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FleetManagement.Enrollment;
using Xunit;

namespace FleetManagement.Tests;

public sealed class DeviceCertificateProofTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("server")]
    [InlineData("extra-san")]
    [InlineData("empty-id")]
    [InlineData("wrong-root")]
    [InlineData("expired")]
    [InlineData("future")]
    public void OnlyExactCurrentlyValidClientIdentityIsAccepted(string scenario)
    {
        var now = DateTimeOffset.UtcNow;
        var deviceId = scenario == "empty-id" ? Guid.Empty : Guid.NewGuid();
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = new CertificateRequest("CN=Device proof test root", rootKey, HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var root = rootRequest.CreateSelfSigned(now.AddDays(-2), now.AddDays(3));
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=not an identity", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
            { new(scenario == "server" ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddUri(new Uri(EnrollmentCertificateVerifier.DeviceUri(deviceId)));
        if (scenario == "extra-san") names.AddDnsName("unexpected.example.test");
        request.CertificateExtensions.Add(names.Build());
        var start = scenario == "future" ? now.AddMinutes(1) : now.AddMinutes(-1);
        var end = scenario == "expired" ? now.AddSeconds(-1) : now.AddDays(1);
        using var certificate = request.Create(root, start, end, RandomNumberGenerator.GetBytes(16));
        using var otherRootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherRoot = new CertificateRequest("CN=Other root", otherRootKey, HashAlgorithmName.SHA256)
            .CreateSelfSigned(now.AddDays(-1), now.AddDays(3));
        if (scenario == "valid")
            Assert.Equal(deviceId, DeviceCertificateProof.Verify(certificate, [], root, now));
        else
            Assert.Throws<InvalidEnrollmentCertificateException>(() => DeviceCertificateProof.Verify(certificate, [],
                scenario == "wrong-root" ? otherRoot : root, now));
    }
}