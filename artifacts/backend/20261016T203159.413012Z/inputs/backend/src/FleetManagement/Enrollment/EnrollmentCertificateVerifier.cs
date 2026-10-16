using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FleetManagement.Enrollment;

public sealed record EnrollmentCertificateResponse(int SchemaVersion, Guid DeviceId, string CertificateDer,
    IReadOnlyList<string> IntermediateCertificatesDer, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

public sealed class InvalidEnrollmentCertificateException : Exception
{
    public InvalidEnrollmentCertificateException() : base("Invalid enrollment certificate.") { }
}

public static class EnrollmentCertificateVerifier
{
    public static EnrollmentCertificateResponse Verify(Guid deviceId, string subjectPublicKeyInfoBase64,
        string certificateDer, IReadOnlyList<string> intermediateCertificatesDer,
        X509Certificate2 trustedRoot, DateTimeOffset now, TimeSpan maximumLifetime)
    {
        ArgumentNullException.ThrowIfNull(trustedRoot);
        if (deviceId == Guid.Empty || maximumLifetime <= TimeSpan.Zero)
            throw new ArgumentException("A device identity and positive certificate lifetime are required.");
        try
        {
            if (intermediateCertificatesDer is null || intermediateCertificatesDer.Count > 4
                || intermediateCertificatesDer.Distinct(StringComparer.Ordinal).Count() != intermediateCertificatesDer.Count)
                throw new InvalidEnrollmentCertificateException();
            using var certificate = Load(certificateDer);
            using var key = certificate.GetECDsaPublicKey();
            if (key is null || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7"
                || certificate.SignatureAlgorithm.Value != "1.2.840.10045.4.3.2"
                || Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) != subjectPublicKeyInfoBase64)
                throw new InvalidEnrollmentCertificateException();
            var issuedAt = new DateTimeOffset(certificate.NotBefore.ToUniversalTime());
            var expiresAt = new DateTimeOffset(certificate.NotAfter.ToUniversalTime());
            if (now < issuedAt || now >= expiresAt || expiresAt <= issuedAt || expiresAt - issuedAt > maximumLifetime)
                throw new InvalidEnrollmentCertificateException();
            var constraints = certificate.Extensions.OfType<X509BasicConstraintsExtension>().SingleOrDefault();
            var usage = certificate.Extensions.OfType<X509KeyUsageExtension>().SingleOrDefault();
            var enhancedUsage = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().SingleOrDefault();
            var names = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().SingleOrDefault();
            if (constraints is null || constraints.CertificateAuthority || usage is null
                || usage.KeyUsages != X509KeyUsageFlags.DigitalSignature || enhancedUsage is null
                || enhancedUsage.EnhancedKeyUsages.Count != 1
                || enhancedUsage.EnhancedKeyUsages[0].Value != "1.3.6.1.5.5.7.3.2"
                || names is null || !names.EnumerateUris().SequenceEqual([DeviceUri(deviceId)])
                || names.EnumerateDnsNames().Any() || names.EnumerateIPAddresses().Any())
                throw new InvalidEnrollmentCertificateException();
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(trustedRoot);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.DisableCertificateDownloads = true;
            chain.ChainPolicy.VerificationTime = now.UtcDateTime;
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.2"));
            var intermediates = new List<X509Certificate2>();
            try
            {
                foreach (var encoded in intermediateCertificatesDer)
                {
                    var intermediate = Load(encoded);
                    intermediates.Add(intermediate);
                    if (intermediate.RawData.AsSpan().SequenceEqual(trustedRoot.RawData)
                        || intermediate.RawData.AsSpan().SequenceEqual(certificate.RawData))
                        throw new InvalidEnrollmentCertificateException();
                    chain.ChainPolicy.ExtraStore.Add(intermediate);
                }
                if (!chain.Build(certificate) || chain.ChainElements.Count != intermediates.Count + 2
                    || !chain.ChainElements[^1].Certificate.RawData.AsSpan().SequenceEqual(trustedRoot.RawData))
                    throw new InvalidEnrollmentCertificateException();
                for (var index = 0; index < intermediates.Count; index++)
                    if (!chain.ChainElements[index + 1].Certificate.RawData.AsSpan().SequenceEqual(intermediates[index].RawData))
                        throw new InvalidEnrollmentCertificateException();
                return new EnrollmentCertificateResponse(1, deviceId, certificateDer,
                    Array.AsReadOnly(intermediateCertificatesDer.ToArray()), issuedAt, expiresAt);
            }
            finally
            {
                foreach (var intermediate in intermediates)
                    intermediate.Dispose();
            }
        }
        catch (Exception error) when (error is CryptographicException or FormatException
            or ArgumentException or InvalidOperationException)
        {
            throw new InvalidEnrollmentCertificateException();
        }
    }

    public static string DeviceUri(Guid deviceId) => $"urn:fleet:device:{deviceId:D}";

    private static X509Certificate2 Load(string encoded)
    {
        if (encoded is null || encoded.Length is < 64 or > 16384)
            throw new InvalidEnrollmentCertificateException();
        var bytes = Convert.FromBase64String(encoded);
        if (Convert.ToBase64String(bytes) != encoded)
            throw new InvalidEnrollmentCertificateException();
        var certificate = X509CertificateLoader.LoadCertificate(bytes);
        if (!certificate.RawData.AsSpan().SequenceEqual(bytes))
        {
            certificate.Dispose();
            throw new InvalidEnrollmentCertificateException();
        }
        return certificate;
    }
}