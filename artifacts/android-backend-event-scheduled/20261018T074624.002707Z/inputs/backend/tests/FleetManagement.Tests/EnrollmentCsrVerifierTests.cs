using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FleetManagement.Enrollment;
using Xunit;

namespace FleetManagement.Tests;

public sealed class EnrollmentCsrVerifierTests
{
    [Fact]
    public void ValidP256Sha256CsrReturnsVerifiedKeyAndExactDigest()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var encoded = CreateRequest(key, HashAlgorithmName.SHA256);

        var verified = EnrollmentCsrVerifier.Verify(Convert.ToBase64String(encoded));

        Assert.Equal(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), verified.SubjectPublicKeyInfoBase64);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(encoded)), verified.CsrSha256);
    }

    [Fact]
    public void RequestedSubjectAndSanAreNotReturnedAsAuthority()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=administrator", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddUri(new Uri("urn:fleet:administrator"));
        request.CertificateExtensions.Add(names.Build());

        var verified = EnrollmentCsrVerifier.Verify(Convert.ToBase64String(request.CreateSigningRequest()));

        Assert.Equal(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), verified.SubjectPublicKeyInfoBase64);
        Assert.Equal(2, typeof(VerifiedEnrollmentKey).GetProperties().Length);
    }

    [Fact]
    public void TamperedSignatureIsRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var encoded = CreateRequest(key, HashAlgorithmName.SHA256);
        encoded[^1] ^= 1;
        AssertRejected(Convert.ToBase64String(encoded));
    }

    [Fact]
    public void WrongSigningKeyIsRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var reader = new AsnReader(CreateRequest(key, HashAlgorithmName.SHA256), AsnEncodingRules.DER).ReadSequence();
        var information = reader.ReadEncodedValue();
        var algorithm = reader.ReadEncodedValue();
        var signature = otherKey.SignData(information.Span, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteEncodedValue(information.Span);
            writer.WriteEncodedValue(algorithm.Span);
            writer.WriteBitString(signature);
        }
        AssertRejected(Convert.ToBase64String(writer.Encode()));
    }

    [Fact]
    public void TrailingDerDataIsRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var encoded = CreateRequest(key, HashAlgorithmName.SHA256);
        AssertRejected(Convert.ToBase64String([.. encoded, 0x05, 0x00]));
    }

    [Fact]
    public void Sha384SignatureIsRejectedEvenWithP256Key()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        AssertRejected(Convert.ToBase64String(CreateRequest(key, HashAlgorithmName.SHA384)));
    }

    [Fact]
    public void ConcatenatedRequestsAreRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var encoded = CreateRequest(key, HashAlgorithmName.SHA256);
        AssertRejected(Convert.ToBase64String([.. encoded, .. encoded]));
    }

    [Fact]
    public void PemIsRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=lab", key, HashAlgorithmName.SHA256);
        AssertRejected(request.CreateSigningRequestPem());
    }

    [Fact]
    public void BerIndefiniteLengthIsRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var encoded = CreateRequest(key, HashAlgorithmName.SHA256);
        var sequence = new AsnReader(encoded, AsnEncodingRules.DER).ReadSequence();
        var contents = sequence.PeekContentBytes().ToArray();
        AssertRejected(Convert.ToBase64String([0x30, 0x80, .. contents, 0x00, 0x00]));
    }

    [Fact]
    public void NoncanonicalBase64PaddingBitsAreRejected()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var request = new CertificateRequest($"CN=lab-{attempt}", key, HashAlgorithmName.SHA256);
            var encoded = Convert.ToBase64String(request.CreateSigningRequest());
            if (!encoded.EndsWith('='))
                continue;
            var lastDataIndex = encoded.TrimEnd('=').Length - 1;
            var noncanonical = encoded.ToCharArray();
            noncanonical[lastDataIndex] = alphabet[alphabet.IndexOf(encoded[lastDataIndex]) + 1];
            var altered = new string(noncanonical);
            Assert.Equal(Convert.FromBase64String(encoded), Convert.FromBase64String(altered));
            EnrollmentCsrVerifier.Verify(encoded);
            AssertRejected(altered);
            return;
        }
        Assert.Fail("Could not generate a padded signing request.");
    }

    [Fact]
    public void P384KeyIsRejectedEvenWithSha256Signature()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        AssertRejected(Convert.ToBase64String(CreateRequest(key, HashAlgorithmName.SHA256)));
    }

    [Fact]
    public void RsaKeyIsRejected()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=lab", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        AssertRejected(Convert.ToBase64String(request.CreateSigningRequest()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("QUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFB")]
    public void InvalidEncodingOrNonCsrIsRejected(string? value) => AssertRejected(value);

    [Fact]
    public void WhitespaceIsRejectedEvenWhenBase64DecoderWouldAcceptIt()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        AssertRejected(Convert.ToBase64String(CreateRequest(key, HashAlgorithmName.SHA256)) + "\n");
    }

    [Theory]
    [InlineData(60)]
    [InlineData(16388)]
    public void EncodedSizeLimitsAreEnforced(int length) => AssertRejected(new string('A', length));

    private static byte[] CreateRequest(ECDsa key, HashAlgorithmName algorithm) =>
        new CertificateRequest("CN=untrusted-request", key, algorithm).CreateSigningRequest();

    private static void AssertRejected(string? encoded)
    {
        var error = Assert.Throws<InvalidEnrollmentCsrException>(() => EnrollmentCsrVerifier.Verify(encoded));
        Assert.Equal("Invalid enrollment CSR.", error.Message);
        Assert.Null(error.InnerException);
    }
}