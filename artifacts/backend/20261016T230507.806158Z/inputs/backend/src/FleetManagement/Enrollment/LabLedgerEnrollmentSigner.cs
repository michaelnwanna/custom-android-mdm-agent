using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace FleetManagement.Enrollment;

public sealed class LabLedgerEnrollmentSigner : IEnrollmentSigner
{
    private readonly string directory;
    private readonly X509Certificate2 issuer;
    private readonly TimeProvider clock;
    private readonly TimeSpan lifetime;

    public LabLedgerEnrollmentSigner(string ledgerDirectory, X509Certificate2 issuer, TimeProvider clock, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(clock);
        if (!Path.IsPathFullyQualified(ledgerDirectory) || !issuer.HasPrivateKey
            || lifetime < TimeSpan.FromMinutes(1) || lifetime > TimeSpan.FromDays(30))
            throw new ArgumentException("A private lab ledger, signing issuer and bounded lifetime are required.");
        using var key = issuer.GetECDsaPrivateKey();
        var constraints = issuer.Extensions.OfType<X509BasicConstraintsExtension>().SingleOrDefault();
        var usage = issuer.Extensions.OfType<X509KeyUsageExtension>().SingleOrDefault();
        if (key is null || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7"
            || constraints?.CertificateAuthority != true || usage is null
            || !usage.KeyUsages.HasFlag(X509KeyUsageFlags.KeyCertSign))
            throw new ArgumentException("The lab issuer must be a P-256 certificate authority.");
        directory = Path.GetFullPath(ledgerDirectory);
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        this.issuer = issuer;
        this.clock = clock;
        this.lifetime = lifetime;
    }

    public Task<EnrollmentSignerLookup> LookupAsync(EnrollmentSigningRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(request);
        using var operationLock = OpenLock(request.OperationId);
        return Task.FromResult(Read(request));
    }

    public Task<SignedEnrollmentCertificate> IssueAsync(EnrollmentSigningRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(request);
        using var operationLock = OpenLock(request.OperationId);
        var existing = Read(request);
        if (existing.State == EnrollmentSignerState.Issued)
            return Task.FromResult(existing.Certificate!);
        if (existing.State == EnrollmentSignerState.Unknown)
            throw new InvalidOperationException("The signing operation is unresolved; automatic reissuance is forbidden.");
        var now = clock.GetUtcNow();
        var notBefore = new DateTimeOffset(now.UtcDateTime.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond)));
        var notAfter = notBefore.Add(lifetime);
        if (notBefore < issuer.NotBefore.ToUniversalTime() || notAfter > issuer.NotAfter.ToUniversalTime())
            throw new InvalidOperationException("The lab issuer cannot cover the requested validity.");
        using var publicKey = ECDsa.Create();
        publicKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(request.SubjectPublicKeyInfoBase64), out _);
        var certificate = new CertificateRequest($"CN={request.DeviceId:D}", publicKey, HashAlgorithmName.SHA256);
        certificate.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        certificate.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        certificate.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddUri(new Uri(EnrollmentCertificateVerifier.DeviceUri(request.DeviceId)));
        certificate.CertificateExtensions.Add(names.Build());
        WriteNew(Filename(request.OperationId, "reserved"), new Reservation(request, Convert.ToBase64String(issuer.RawData)));
        cancellationToken.ThrowIfCancellationRequested();
        using var signed = certificate.Create(issuer, notBefore, notAfter, RandomNumberGenerator.GetBytes(16));
        var result = new SignedEnrollmentCertificate(request, Convert.ToBase64String(signed.RawData), []);
        var temporary = Filename(request.OperationId, "partial");
        WriteNew(temporary, result);
        File.Move(temporary, Filename(request.OperationId, "issued"), overwrite: false);
        return Task.FromResult(result);
    }

    private EnrollmentSignerLookup Read(EnrollmentSigningRequest request)
    {
        var reserved = Filename(request.OperationId, "reserved");
        var issued = Filename(request.OperationId, "issued");
        if (!File.Exists(reserved))
        {
            if (File.Exists(issued) || File.Exists(Filename(request.OperationId, "partial")))
                throw new InvalidOperationException("The lab ledger is inconsistent.");
            return new EnrollmentSignerLookup(EnrollmentSignerState.NotFound);
        }
        var reservation = JsonSerializer.Deserialize<Reservation>(File.ReadAllBytes(reserved))
            ?? throw new InvalidOperationException("The lab reservation is invalid.");
        if (reservation.Request != request || reservation.IssuerDer != Convert.ToBase64String(issuer.RawData))
            throw new InvalidOperationException("The lab operation binding or issuer does not match.");
        if (!File.Exists(issued))
            return new EnrollmentSignerLookup(EnrollmentSignerState.Unknown);
        var result = JsonSerializer.Deserialize<SignedEnrollmentCertificate>(File.ReadAllBytes(issued))
            ?? throw new InvalidOperationException("The lab result is invalid.");
        if (result.Request != request)
            throw new InvalidOperationException("The lab result does not match its operation.");
        return new EnrollmentSignerLookup(EnrollmentSignerState.Issued, result);
    }

    private FileStream OpenLock(Guid operationId) => new(Filename(operationId, "lock"), Options(FileMode.OpenOrCreate));

    private static FileStreamOptions Options(FileMode mode)
    {
        var options = new FileStreamOptions { Mode = mode, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return options;
    }

    private string Filename(Guid operationId, string suffix) => Path.Combine(directory, $"{operationId:N}.{suffix}.json");

    private static void WriteNew<T>(string filename, T value)
    {
        using var stream = new FileStream(filename, Options(FileMode.CreateNew));
        JsonSerializer.Serialize(stream, value);
        stream.Flush(flushToDisk: true);
    }

    private static void Validate(EnrollmentSigningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.OperationId == Guid.Empty || request.DeviceId == Guid.Empty || request.CsrSha256 is not { Length: 64 }
            || !request.CsrSha256.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
            || request.SubjectPublicKeyInfoBase64 is not { Length: > 0 and <= 512 })
            throw new ArgumentException("Invalid signing operation binding.");
        var bytes = Convert.FromBase64String(request.SubjectPublicKeyInfoBase64);
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(bytes, out var consumed);
        if (consumed != bytes.Length || Convert.ToBase64String(bytes) != request.SubjectPublicKeyInfoBase64
            || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
            throw new ArgumentException("Invalid signing public key.");
    }

    private sealed record Reservation(EnrollmentSigningRequest Request, string IssuerDer);
}