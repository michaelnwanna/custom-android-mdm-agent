using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace FleetManagement.Postgres.Tests;

public sealed class EnrollmentSigningTests
{
    [Theory]
    [InlineData("concurrent")]
    [InlineData("unknown-lookup")]
    [InlineData("lost-reply")]
    [InlineData("stale-not-found")]
    [InlineData("failed-completion")]
    [InlineData("failure-before-signing")]
    [InlineData("wrong-binding")]
    [InlineData("invalid-certificate")]
    [InlineData("lab-ledger")]
    public async Task SigningReconcilesExistingOperationWithoutBlindReissuance(string scenario)
    {
        var connectionString = Environment.GetEnvironmentVariable("FLEET_TEST_POSTGRES");
        Assert.False(string.IsNullOrWhiteSpace(connectionString), "An authorized disposable PostgreSQL database is required.");
        var schema = "fleet_test_" + Guid.NewGuid().ToString("N");
        var created = false;
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection))
                await create.ExecuteNonQueryAsync();
            created = true;
            var scoped = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
            var fault = new CompletionFault();
            var factory = new ContextFactory(new DbContextOptionsBuilder<FleetDbContext>()
                .UseNpgsql(scoped.ConnectionString).AddInterceptors(fault).Options);
            await using (var context = factory.CreateDbContext())
                await context.Database.MigrateAsync();
            var clock = new FixedClock();
            var claims = new PostgresEnrollmentClaims(factory, clock);
            var digestKey = RandomNumberGenerator.GetBytes(32);
            var token = new EnrollmentBootstrapTokens(clock).Issue(Guid.NewGuid(), digestKey, TimeSpan.FromMinutes(5));
            await claims.RegisterAsync(token.Credential);
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var csr = Convert.ToBase64String(new CertificateRequest("CN=ignored", key, HashAlgorithmName.SHA256).CreateSigningRequest());
            var pendingId = token.Credential.PendingEnrollmentId;
            var claim = await claims.ClaimAsync(pendingId, token.Secret, csr, digestKey);
            var operationId = claim.OperationId!.Value;
            using var signer = new TestSigner(clock.GetUtcNow());
            if (scenario == "lab-ledger")
            {
                var ledger = Path.Combine(Path.GetTempPath(), "fleet-signer-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var adapter = new LabLedgerEnrollmentSigner(ledger, signer.Root, clock, TimeSpan.FromDays(1));
                    var labWorker = new EnrollmentSigningCoordinator(claims, adapter, signer.Root, TimeSpan.FromDays(2));
                    fault.FailCompletion = true;
                    await Assert.ThrowsAsync<InvalidOperationException>(() => labWorker.RunAsync(pendingId, operationId));
                    var work = await claims.ReadSigningWorkAsync(pendingId, operationId);
                    Assert.Null(work.Response);
                    var reopenedAdapter = new LabLedgerEnrollmentSigner(ledger, signer.Root, clock, TimeSpan.FromDays(1));
                    var issued = await reopenedAdapter.LookupAsync(work.Request, default);
                    Assert.Equal(EnrollmentSignerState.Issued, issued.State);
                    var reopenedClaims = new PostgresEnrollmentClaims(factory, clock);
                    var reopenedWorker = new EnrollmentSigningCoordinator(reopenedClaims, reopenedAdapter, signer.Root, TimeSpan.FromDays(2));
                    var recovered = await reopenedWorker.RunAsync(pendingId, operationId);
                    Assert.Equal(issued.Certificate!.CertificateDer, recovered!.CertificateDer);
                    Assert.Single(Directory.GetFiles(ledger, "*.issued.json"));
                    var authorizedRetry = await reopenedClaims.ClaimAsync(pendingId, token.Secret, csr, digestKey);
                    Assert.Equal(EnrollmentClaimStatus.Completed, authorizedRetry.Status);
                    Assert.Equal(recovered.CertificateDer, authorizedRetry.Response!.CertificateDer);
                }
                finally
                {
                    if (Directory.Exists(ledger))
                        Directory.Delete(ledger, recursive: true);
                }
                return;
            }
            var worker = new EnrollmentSigningCoordinator(claims, signer, signer.Root, TimeSpan.FromDays(2));
            await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RunAsync(pendingId, Guid.NewGuid()));
            Assert.Equal(0, signer.LookupCalls);
            if (scenario == "concurrent")
            {
                var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => worker.RunAsync(pendingId, operationId)));
                Assert.Contains(results, result => result is not null);
            }
            else if (scenario == "unknown-lookup")
            {
                signer.Unknown = true;
                Assert.Null(await worker.RunAsync(pendingId, operationId));
                Assert.Equal(0, signer.IssueCalls);
                await using (var context = factory.CreateDbContext())
                    Assert.False((await context.PendingEnrollments.SingleAsync()).SigningAttempted);
                signer.Unknown = false;
            }
            else
            {
                signer.LoseReply = scenario is "lost-reply" or "stale-not-found";
                signer.FailBeforeSigning = scenario == "failure-before-signing";
                signer.WrongBinding = scenario == "wrong-binding";
                signer.InvalidCertificate = scenario == "invalid-certificate";
                fault.FailCompletion = scenario == "failed-completion";
                if (scenario is "lost-reply" or "stale-not-found" or "failure-before-signing")
                    await Assert.ThrowsAsync<IOException>(() => worker.RunAsync(pendingId, operationId));
                else if (scenario == "invalid-certificate")
                    await Assert.ThrowsAsync<InvalidEnrollmentCertificateException>(() => worker.RunAsync(pendingId, operationId));
                else
                    await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RunAsync(pendingId, operationId));
                await using (var context = factory.CreateDbContext())
                {
                    var pending = await context.PendingEnrollments.SingleAsync();
                    Assert.True(pending.SigningAttempted);
                    Assert.Null(pending.CompletedResponseJson);
                }
                if (scenario == "stale-not-found")
                {
                    signer.HideIssued = true;
                    Assert.Null(await worker.RunAsync(pendingId, operationId));
                    Assert.Equal(1, signer.IssueCalls);
                    signer.HideIssued = false;
                }
                if (scenario == "failure-before-signing")
                {
                    Assert.Null(await worker.RunAsync(pendingId, operationId));
                    Assert.Equal(1, signer.IssueCalls);
                    return;
                }
                if (scenario is "wrong-binding" or "invalid-certificate")
                {
                    if (scenario == "wrong-binding")
                        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RunAsync(pendingId, operationId));
                    else
                        await Assert.ThrowsAsync<InvalidEnrollmentCertificateException>(() => worker.RunAsync(pendingId, operationId));
                    Assert.Equal(1, signer.IssueCalls);
                    return;
                }
            }
            var reopened = new PostgresEnrollmentClaims(factory, clock);
            var restartedWorker = new EnrollmentSigningCoordinator(reopened, signer, signer.Root, TimeSpan.FromDays(2));
            var completed = await restartedWorker.RunAsync(pendingId, operationId);
            Assert.NotNull(completed);
            Assert.Equal(1, signer.IssueCalls);
            Assert.Equal(claim.DeviceId, completed.DeviceId);
            Assert.Equal(signer.Issued!.CertificateDer, completed.CertificateDer);
            Assert.All(signer.Requests, request =>
            {
                Assert.Equal(operationId, request.OperationId);
                Assert.Equal(claim.DeviceId, request.DeviceId);
                Assert.Equal(EnrollmentCsrVerifier.Verify(csr).CsrSha256, request.CsrSha256);
                Assert.Equal(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), request.SubjectPublicKeyInfoBase64);
            });
            var lookupCalls = signer.LookupCalls;
            var retry = await restartedWorker.RunAsync(pendingId, operationId);
            Assert.Equal(completed.CertificateDer, retry!.CertificateDer);
            Assert.Equal(lookupCalls, signer.LookupCalls);
            var authorized = await reopened.ClaimAsync(pendingId, token.Secret, csr, digestKey);
            Assert.Equal(EnrollmentClaimStatus.Completed, authorized.Status);
            Assert.Equal(completed.CertificateDer, authorized.Response!.CertificateDer);
        }
        catch (NpgsqlException)
        {
            throw new InvalidOperationException("PostgreSQL signing check failed; connection details are redacted.");
        }
        finally
        {
            if (created)
            {
                try
                {
                    await using var cleanup = new NpgsqlConnection(connectionString);
                    await cleanup.OpenAsync();
                    await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", cleanup);
                    await drop.ExecuteNonQueryAsync();
                }
                catch (NpgsqlException)
                {
                    throw new InvalidOperationException("Signing test cleanup failed; connection details are redacted.");
                }
            }
        }
    }

    private sealed class ContextFactory(DbContextOptions<FleetDbContext> options) : IDbContextFactory<FleetDbContext>
    {
        public FleetDbContext CreateDbContext() => new(options);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class CompletionFault : SaveChangesInterceptor
    {
        public bool FailCompletion { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (FailCompletion && eventData.Context!.ChangeTracker.Entries<PendingEnrollment>()
                .Any(entry => entry.Entity.CompletedResponseJson is not null))
            {
                FailCompletion = false;
                throw new InvalidOperationException("Simulated completion persistence failure.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TestSigner : IEnrollmentSigner, IDisposable
    {
        private readonly ECDsa rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly DateTimeOffset now;
        public X509Certificate2 Root { get; }
        public ConcurrentBag<EnrollmentSigningRequest> Requests { get; } = [];
        public SignedEnrollmentCertificate? Issued { get; private set; }
        public int IssueCalls;
        public int LookupCalls;
        public bool Unknown { get; set; }
        public bool HideIssued { get; set; }
        public bool LoseReply { get; set; }
        public bool FailBeforeSigning { get; set; }
        public bool WrongBinding { get; set; }
        public bool InvalidCertificate { get; set; }

        public TestSigner(DateTimeOffset currentTime)
        {
            now = currentTime;
            var request = new CertificateRequest("CN=Signing test root", rootKey, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            Root = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(7));
        }

        public Task<EnrollmentSignerLookup> LookupAsync(EnrollmentSigningRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref LookupCalls);
            Requests.Add(request);
            return Task.FromResult(Unknown ? new EnrollmentSignerLookup(EnrollmentSignerState.Unknown)
                : Issued is null || HideIssued ? new EnrollmentSignerLookup(EnrollmentSignerState.NotFound)
                : new EnrollmentSignerLookup(EnrollmentSignerState.Issued, Issued));
        }

        public Task<SignedEnrollmentCertificate> IssueAsync(EnrollmentSigningRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref IssueCalls);
            Requests.Add(request);
            if (FailBeforeSigning)
                throw new IOException("Simulated unavailable signer.");
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(request.SubjectPublicKeyInfoBase64), out _);
            var certificate = new CertificateRequest("CN=server controlled", key, HashAlgorithmName.SHA256);
            certificate.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            certificate.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            certificate.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true));
            var names = new SubjectAlternativeNameBuilder();
            names.AddUri(new Uri(EnrollmentCertificateVerifier.DeviceUri(request.DeviceId)));
            certificate.CertificateExtensions.Add(names.Build());
            using var leaf = certificate.Create(Root, now.AddMinutes(-1), now.AddDays(1), RandomNumberGenerator.GetBytes(16));
            Issued = new SignedEnrollmentCertificate(WrongBinding ? request with { OperationId = Guid.NewGuid() } : request,
                InvalidCertificate ? "invalid" : Convert.ToBase64String(leaf.RawData), []);
            if (LoseReply)
                throw new IOException("Simulated lost signer reply after issuance.");
            return Task.FromResult(Issued);
        }

        public void Dispose()
        {
            Root.Dispose();
            rootKey.Dispose();
        }
    }
}