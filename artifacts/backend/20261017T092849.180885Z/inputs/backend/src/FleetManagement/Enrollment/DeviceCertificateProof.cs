using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FleetManagement.Enrollment;

public static class DeviceCertificateProof
{
    public static Guid Verify(X509Certificate2 certificate, IReadOnlyList<string> intermediates,
        X509Certificate2 trustedRoot, DateTimeOffset now)
    {
        var deviceId = ReadClaimedDeviceId(certificate);
        using var key = certificate.GetECDsaPublicKey();
        if (key is null)
            throw new InvalidEnrollmentCertificateException();
        EnrollmentCertificateVerifier.Verify(deviceId, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
            Convert.ToBase64String(certificate.RawData), intermediates, trustedRoot, now, TimeSpan.FromDays(2));
        return deviceId;
    }

    internal static Guid ReadClaimedDeviceId(X509Certificate2 certificate)
    {
        try
        {
            var names = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
            var reader = new AsnReader(names.RawData, AsnEncodingRules.DER);
            var sequence = reader.ReadSequence();
            var identity = sequence.ReadCharacterString(UniversalTagNumber.IA5String, new Asn1Tag(TagClass.ContextSpecific, 6));
            sequence.ThrowIfNotEmpty();
            reader.ThrowIfNotEmpty();
            const string prefix = "urn:fleet:device:";
            if (!identity.StartsWith(prefix, StringComparison.Ordinal)
                || !Guid.TryParseExact(identity[prefix.Length..], "D", out var deviceId)
                || identity != EnrollmentCertificateVerifier.DeviceUri(deviceId) || deviceId == Guid.Empty)
                throw new InvalidEnrollmentCertificateException();
            return deviceId;
        }
        catch (Exception error) when (error is AsnContentException or InvalidOperationException or CryptographicException
            or ArgumentException)
        {
            throw new InvalidEnrollmentCertificateException();
        }
    }
}