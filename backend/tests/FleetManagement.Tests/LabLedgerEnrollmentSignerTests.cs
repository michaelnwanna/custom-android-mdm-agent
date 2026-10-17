using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FleetManagement.Enrollment;
using Xunit;

namespace FleetManagement.Tests;

public sealed class LabLedgerEnrollmentSignerTests
{
    [Fact]
    public async Task ReopenedAdapterRecoversOriginalValidCertificateAndRejectsChangedBinding()
    {
        using var fixture = new Fixture();
        var signer = fixture.Open();
        Assert.Equal(EnrollmentSignerState.NotFound, (await signer.LookupAsync(fixture.Request, default)).State);
        var first = await signer.IssueAsync(fixture.Request, default);
        var reopened = fixture.Open();
        var lookup = await reopened.LookupAsync(fixture.Request, default);
        Assert.Equal(EnrollmentSignerState.Issued, lookup.State);
        Assert.Equal(first.CertificateDer, lookup.Certificate!.CertificateDer);
        Assert.Equal(first.CertificateDer, (await reopened.IssueAsync(fixture.Request, default)).CertificateDer);
        using var leaf = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(first.CertificateDer));
        Assert.Single(leaf.Extensions.OfType<X509SubjectKeyIdentifierExtension>());
        Assert.Single(leaf.Extensions.OfType<X509AuthorityKeyIdentifierExtension>());
        var verified = EnrollmentCertificateVerifier.Verify(fixture.Request.DeviceId,
            fixture.Request.SubjectPublicKeyInfoBase64, first.CertificateDer, [], fixture.Root,
            fixture.Clock.GetUtcNow(), TimeSpan.FromDays(2));
        Assert.Equal(fixture.Clock.GetUtcNow().AddSeconds(-30), verified.IssuedAt);
        Assert.Equal(verified.IssuedAt.AddDays(1), verified.ExpiresAt);
        EnrollmentCertificateVerifier.Verify(fixture.Request.DeviceId,
            fixture.Request.SubjectPublicKeyInfoBase64, first.CertificateDer, [], fixture.Root,
            fixture.Clock.GetUtcNow().AddSeconds(-2), TimeSpan.FromDays(2));
        Assert.Throws<InvalidEnrollmentCertificateException>(() => EnrollmentCertificateVerifier.Verify(fixture.Request.DeviceId,
            fixture.Request.SubjectPublicKeyInfoBase64, first.CertificateDer, [], fixture.Root,
            fixture.Clock.GetUtcNow().AddSeconds(-31), TimeSpan.FromDays(2)));
        Assert.Throws<InvalidEnrollmentCertificateException>(() => EnrollmentCertificateVerifier.Verify(fixture.Request.DeviceId,
            fixture.Request.SubjectPublicKeyInfoBase64, first.CertificateDer, [], fixture.Root,
            verified.ExpiresAt, TimeSpan.FromDays(2)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.LookupAsync(
            fixture.Request with { DeviceId = Guid.NewGuid() }, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.IssueAsync(
            fixture.Request with { CsrSha256 = new string('b', 64) }, default));
        using var other = new Fixture();
        var changedIssuer = new LabLedgerEnrollmentSigner(fixture.Directory, other.Root, fixture.Clock, TimeSpan.FromDays(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => changedIssuer.LookupAsync(fixture.Request, default));
    }

    [Fact]
    public async Task ConcurrentAdaptersNeverPublishReplacementCertificates()
    {
        using var fixture = new Fixture();
        var attempts = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            try
            {
                return await fixture.Open().IssueAsync(fixture.Request, default);
            }
            catch (IOException)
            {
                return null;
            }
        }));
        var results = await Task.WhenAll(attempts);
        var issued = (await fixture.Open().LookupAsync(fixture.Request, default)).Certificate!;
        Assert.Contains(results, result => result is not null);
        Assert.All(results.Where(result => result is not null), result => Assert.Equal(issued.CertificateDer, result!.CertificateDer));
        Assert.Single(System.IO.Directory.GetFiles(fixture.Directory, "*.issued.json"));
    }

    [Fact]
    public async Task ReservedOperationWithoutPublishedResultRemainsUnknownAndCannotReissue()
    {
        using var fixture = new Fixture();
        await fixture.Open().IssueAsync(fixture.Request, default);
        File.Delete(Path.Combine(fixture.Directory, $"{fixture.Request.OperationId:N}.issued.json"));
        var reopened = fixture.Open();
        Assert.Equal(EnrollmentSignerState.Unknown, (await reopened.LookupAsync(fixture.Request, default)).State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.IssueAsync(fixture.Request, default));
        Assert.False(File.Exists(Path.Combine(fixture.Directory, $"{fixture.Request.OperationId:N}.issued.json")));
    }

    [Fact]
    public async Task ExclusiveLockFailsClosedAndCanceledCallDoesNotReserve()
    {
        using var fixture = new Fixture();
        var signer = fixture.Open();
        using (var operationLock = new FileStream(Path.Combine(fixture.Directory,
                   $"{fixture.Request.OperationId:N}.lock.json"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAsync<IOException>(() => signer.IssueAsync(fixture.Request, default));
        await Assert.ThrowsAsync<OperationCanceledException>(() => signer.IssueAsync(fixture.Request, new CancellationToken(true)));
        Assert.Equal(EnrollmentSignerState.NotFound, (await signer.LookupAsync(fixture.Request, default)).State);
    }

    [Fact]
    public async Task InvalidBindingAndInvalidIssuerDoNotCreateReservation()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Open().IssueAsync(fixture.Request with { OperationId = Guid.Empty }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Open().IssueAsync(fixture.Request with { CsrSha256 = "bad" }, default));
        Assert.Throws<ArgumentException>(() => new LabLedgerEnrollmentSigner(fixture.Directory,
            fixture.Root, fixture.Clock, TimeSpan.FromDays(31)));
        using var publicRoot = X509CertificateLoader.LoadCertificate(fixture.Root.RawData);
        Assert.Throws<ArgumentException>(() => new LabLedgerEnrollmentSigner(fixture.Directory,
            publicRoot, fixture.Clock, TimeSpan.FromDays(1)));
        Assert.Empty(System.IO.Directory.GetFiles(fixture.Directory, "*.reserved.json"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ECDsa rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "fleet-signer-" + Guid.NewGuid().ToString("N"));
        public FixedClock Clock { get; } = new();
        public X509Certificate2 Root { get; }
        public EnrollmentSigningRequest Request { get; }

        public Fixture()
        {
            var request = new CertificateRequest("CN=Ledger lab root", rootKey, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            Root = request.CreateSelfSigned(Clock.GetUtcNow().AddDays(-1), Clock.GetUtcNow().AddDays(7));
            Request = new EnrollmentSigningRequest(Guid.NewGuid(), Guid.NewGuid(), new string('a', 64),
                Convert.ToBase64String(deviceKey.ExportSubjectPublicKeyInfo()));
            System.IO.Directory.CreateDirectory(Directory);
        }

        public LabLedgerEnrollmentSigner Open() => new(Directory, Root, Clock, TimeSpan.FromDays(1));

        public void Dispose()
        {
            Root.Dispose();
            rootKey.Dispose();
            deviceKey.Dispose();
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    }
}