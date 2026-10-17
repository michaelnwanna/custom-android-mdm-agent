using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FleetManagement.Enrollment;

public sealed record VerifiedEnrollmentKey(string SubjectPublicKeyInfoBase64, string CsrSha256);

public sealed class InvalidEnrollmentCsrException : Exception
{
    public InvalidEnrollmentCsrException() : base("Invalid enrollment CSR.") { }
}

public static class EnrollmentCsrVerifier
{
    private const string EcdsaSha256Oid = "1.2.840.10045.4.3.2";
    private const string P256Oid = "1.2.840.10045.3.1.7";

    public static VerifiedEnrollmentKey Verify(string? csrDer)
    {
        if (csrDer is null || csrDer.Length is < 64 or > 16384)
            throw new InvalidEnrollmentCsrException();

        try
        {
            var encoded = Convert.FromBase64String(csrDer);
            if (!string.Equals(Convert.ToBase64String(encoded), csrDer, StringComparison.Ordinal))
                throw new InvalidEnrollmentCsrException();

            var reader = new AsnReader(encoded, AsnEncodingRules.DER);
            var sequence = reader.ReadSequence();
            reader.ThrowIfNotEmpty();
            sequence.ReadEncodedValue();
            var algorithm = sequence.ReadSequence();
            if (algorithm.ReadObjectIdentifier() != EcdsaSha256Oid)
                throw new InvalidEnrollmentCsrException();
            algorithm.ThrowIfNotEmpty();
            sequence.ReadBitString(out var unusedBits);
            if (unusedBits != 0)
                throw new InvalidEnrollmentCsrException();
            sequence.ThrowIfNotEmpty();

            var request = CertificateRequest.LoadSigningRequest(
                encoded.AsSpan(), HashAlgorithmName.SHA256, out var consumed,
                CertificateRequestLoadOptions.Default);
            if (consumed != encoded.Length)
                throw new InvalidEnrollmentCsrException();

            using var key = request.PublicKey.GetECDsaPublicKey();
            if (key is null || key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != P256Oid)
                throw new InvalidEnrollmentCsrException();

            return new VerifiedEnrollmentKey(
                Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
                Convert.ToHexStringLower(SHA256.HashData(encoded)));
        }
        catch (Exception error) when (error is FormatException or CryptographicException or AsnContentException or ArgumentException)
        {
            throw new InvalidEnrollmentCsrException();
        }
    }
}