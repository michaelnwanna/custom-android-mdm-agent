using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

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
        var schema = "fleet_test_" + Guid.NewGuid().ToString("N");
        var created = false;
        try
        {
            DateTime initialStart;
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
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
                Assert.True((DateTime)(await start.ExecuteScalarAsync())! > initialStart,
                    "The PostgreSQL postmaster must actually restart.");
            }
            var reopened = new PostgresEnrollmentClaims(factory, clock);
            var retry = await reopened.ClaimAsync(token.Credential.PendingEnrollmentId, token.Secret, csr, digestKey);
            Assert.Equal(EnrollmentClaimStatus.InProgress, retry.Status);
            Assert.Equal(claimed.OperationId, retry.OperationId);
            Assert.Equal(claimed.DeviceId, retry.DeviceId);
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