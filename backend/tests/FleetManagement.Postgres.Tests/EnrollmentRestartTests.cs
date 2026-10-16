using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FleetManagement.Postgres.Tests;

public sealed class EnrollmentRestartTests
{
    [Fact]
    public async Task CommittedClaimSurvivesFastPostgresRestart()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLEET_TEST_POSTGRES");
        var binaryDirectory = Environment.GetEnvironmentVariable("FLEET_TEST_POSTGRES_BIN");
        var dataDirectory = Environment.GetEnvironmentVariable("FLEET_TEST_POSTGRES_DATA");
        Assert.True(!string.IsNullOrWhiteSpace(connectionString)
            && !string.IsNullOrWhiteSpace(binaryDirectory) && !string.IsNullOrWhiteSpace(dataDirectory),
            "Run the restart scenario only through tools/check_postgres.py against its private lab cluster.");
        Assert.True(File.Exists(Path.Combine(dataDirectory!, "..", "restart-enabled")),
            "The private cluster must carry the runner's restart marker.");
        var configured = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.True(Path.IsPathFullyQualified(configured.Host!)
            && Path.GetFileName(configured.Host!).StartsWith("fleet-pg-", StringComparison.Ordinal)
            && configured.Username == "fleet_lab", "Restart requires the private lab socket and role.");
        var schema = "fleet_test_" + Guid.NewGuid().ToString("N");
        var created = false;
        try
        {
            DateTime initialStart;
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var location = new NpgsqlCommand("SHOW data_directory", connection);
                Assert.Equal(Path.GetFullPath(dataDirectory!), (string)(await location.ExecuteScalarAsync())!);
                await using var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection);
                await create.ExecuteNonQueryAsync();
                created = true;
                await using var start = new NpgsqlCommand("SELECT pg_postmaster_start_time()", connection);
                initialStart = (DateTime)(await start.ExecuteScalarAsync())!;
            }
            var scopedConnection = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
            var factory = new ContextFactory(new DbContextOptionsBuilder<FleetDbContext>()
                .UseNpgsql(scopedConnection.ConnectionString).Options);
            await using (var context = factory.CreateDbContext())
                await context.Database.MigrateAsync();
            var clock = new FixedClock();
            var digestKey = RandomNumberGenerator.GetBytes(32);
            var token = new EnrollmentBootstrapTokens(clock).Issue(Guid.NewGuid(), digestKey, TimeSpan.FromMinutes(5));
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var csr = Convert.ToBase64String(new CertificateRequest("CN=untrusted", key, HashAlgorithmName.SHA256)
                .CreateSigningRequest());
            var service = new PostgresEnrollmentClaims(factory, clock);
            await service.RegisterAsync(token.Credential);
            var claimed = await service.ClaimAsync(token.Credential.PendingEnrollmentId, token.Secret, csr, digestKey);
            Assert.Equal(EnrollmentClaimStatus.Claimed, claimed.Status);
            Assert.True(await service.TryBeginSigningAsync(token.Credential.PendingEnrollmentId, claimed.OperationId!.Value));
            var uncertainToken = new EnrollmentBootstrapTokens(clock).Issue(Guid.NewGuid(), digestKey, TimeSpan.FromMinutes(5));
            await service.RegisterAsync(uncertainToken.Credential);
            var uncertainClaim = await service.ClaimAsync(uncertainToken.Credential.PendingEnrollmentId,
                uncertainToken.Secret, csr, digestKey);
            Assert.True(await service.TryBeginSigningAsync(uncertainToken.Credential.PendingEnrollmentId,
                uncertainClaim.OperationId!.Value));

            using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var now = clock.GetUtcNow();
            var rootRequest = new CertificateRequest("CN=Restart test root", rootKey, HashAlgorithmName.SHA256);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            using var root = rootRequest.CreateSelfSigned(now.AddDays(-1), now.AddDays(7));
            var request = new CertificateRequest("CN=server controlled", key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true));
            var names = new SubjectAlternativeNameBuilder();
            names.AddUri(new Uri(EnrollmentCertificateVerifier.DeviceUri(claimed.DeviceId!.Value)));
            request.CertificateExtensions.Add(names.Build());
            using var leaf = request.Create(root, now.AddMinutes(-1), now.AddDays(1), RandomNumberGenerator.GetBytes(16));
            var completed = await service.CompleteAsync(token.Credential.PendingEnrollmentId, claimed.OperationId!.Value,
                Convert.ToBase64String(leaf.RawData), [], root, TimeSpan.FromDays(2));

            NpgsqlConnection.ClearAllPools();
            var processInfo = new ProcessStartInfo(Path.Combine(binaryDirectory!, "pg_ctl"))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in new[] { "-D", dataDirectory!, "-l", Path.Combine(dataDirectory!, "restart.log"),
                         "-w", "-t", "60", "-m", "fast", "restart" })
                processInfo.ArgumentList.Add(argument);
            using (var restart = Process.Start(processInfo))
            {
                Assert.NotNull(restart);
                var output = restart.StandardOutput.ReadToEndAsync();
                var errors = restart.StandardError.ReadToEndAsync();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                await restart.WaitForExitAsync(deadline.Token);
                Console.WriteLine("Private PostgreSQL fast restart:");
                Console.WriteLine(await output);
                Console.WriteLine(await errors);
                Assert.Equal(0, restart.ExitCode);
            }
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var start = new NpgsqlCommand("SELECT pg_postmaster_start_time()", connection);
                var restartedAt = (DateTime)(await start.ExecuteScalarAsync())!;
                Console.WriteLine($"Postmaster start before={initialStart:O}; after={restartedAt:O}");
                Assert.True(restartedAt > initialStart,
                    "The PostgreSQL postmaster must actually restart.");
                await using var settings = new NpgsqlCommand("""
                    SELECT current_setting('listen_addresses') = ''
                        AND current_setting('unix_socket_permissions') = '0700'
                        AND current_setting('fsync') = 'on'
                        AND current_setting('synchronous_commit') = 'on'
                    """, connection);
                Assert.True((bool)(await settings.ExecuteScalarAsync())!,
                    "The restarted cluster must preserve private networking and durability settings.");
            }
            var reopened = new PostgresEnrollmentClaims(factory, clock);
            Assert.False(await reopened.TryBeginSigningAsync(uncertainToken.Credential.PendingEnrollmentId,
                uncertainClaim.OperationId.Value));
            var uncertainWork = await reopened.ReadSigningWorkAsync(uncertainToken.Credential.PendingEnrollmentId,
                uncertainClaim.OperationId.Value);
            Assert.Null(uncertainWork.Response);
            Assert.Equal(uncertainClaim.OperationId, uncertainWork.Request.OperationId);
            Assert.Equal(uncertainClaim.DeviceId, uncertainWork.Request.DeviceId);
            var retry = await reopened.ClaimAsync(token.Credential.PendingEnrollmentId, token.Secret, csr, digestKey);
            Assert.Equal(EnrollmentClaimStatus.Completed, retry.Status);
            Assert.Equal(claimed.OperationId, retry.OperationId);
            Assert.Equal(claimed.DeviceId, retry.DeviceId);
            Assert.Equal(completed.CertificateDer, retry.Response!.CertificateDer);
            Assert.Equal(completed.IssuedAt, retry.Response.IssuedAt);
            Assert.Equal(completed.ExpiresAt, retry.Response.ExpiresAt);
            Assert.Equal(completed.IntermediateCertificatesDer, retry.Response.IntermediateCertificatesDer);
            await using (var context = factory.CreateDbContext())
            {
                var persisted = await context.PendingEnrollments.SingleAsync(value => value.Id == token.Credential.PendingEnrollmentId);
                Assert.Equal(claimed.OperationId, persisted.OperationId);
                Assert.Equal(claimed.DeviceId, persisted.DeviceId);
                Assert.Equal(EnrollmentCsrVerifier.Verify(csr).CsrSha256, persisted.CsrSha256);
                Assert.Equal(EnrollmentCsrVerifier.Verify(csr).SubjectPublicKeyInfoBase64, persisted.SubjectPublicKeyInfoBase64);
                Assert.Equal(token.Credential.IssuedAt, persisted.IssuedAt);
                Assert.Equal(token.Credential.ExpiresAt, persisted.ExpiresAt);
                Assert.Equal(clock.GetUtcNow(), persisted.ClaimedAt);
                Assert.True(persisted.SigningAttempted);
            }
        }
        catch (NpgsqlException)
        {
            throw new InvalidOperationException("PostgreSQL restart integration failed; connection details are redacted.");
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
                    throw new InvalidOperationException("PostgreSQL restart test-schema cleanup failed; connection details are redacted.");
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
}