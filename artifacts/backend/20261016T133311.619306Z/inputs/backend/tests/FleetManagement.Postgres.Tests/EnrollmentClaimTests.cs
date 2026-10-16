using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FleetManagement.Postgres.Tests;

public sealed class EnrollmentClaimTests
{
    [Fact]
    public async Task RealPostgresMigrationConcurrencyRetryExpiryAndRollback()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLEET_TEST_POSTGRES");
        Assert.True(!string.IsNullOrWhiteSpace(connectionString),
            "Set FLEET_TEST_POSTGRES to an authorized disposable PostgreSQL database. This test does not skip.");
        var schema = "fleet_test_" + Guid.NewGuid().ToString("N");
        var created = false;
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection))
                await create.ExecuteNonQueryAsync();
            created = true;
            var scopedConnection = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
            var factory = new ContextFactory(new DbContextOptionsBuilder<FleetDbContext>()
                .UseNpgsql(scopedConnection.ConnectionString).Options);
            await using (var context = factory.CreateDbContext())
                await context.Database.MigrateAsync();

            var clock = new ManualClock();
            var service = new PostgresEnrollmentClaims(factory, clock);
            var digestKey = RandomNumberGenerator.GetBytes(32);
            var pendingId = Guid.NewGuid();
            var token = new EnrollmentBootstrapTokens(clock).Issue(pendingId, digestKey, TimeSpan.FromMinutes(5));
            await service.RegisterAsync(token.Credential);
            var csr = CreateCsr();
            var differentCsr = CreateCsr();
            var contenders = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => service.ClaimAsync(pendingId, token.Secret, csr, digestKey)));
            var winner = Assert.Single(contenders, value => value.Status == EnrollmentClaimStatus.Claimed);
            Assert.Equal(7, contenders.Count(value => value.Status == EnrollmentClaimStatus.InProgress));
            Assert.All(contenders, value =>
            {
                Assert.Equal(winner.OperationId, value.OperationId);
                Assert.Equal(winner.DeviceId, value.DeviceId);
            });

            var reopened = new PostgresEnrollmentClaims(factory, clock);
            var retry = await reopened.ClaimAsync(pendingId, token.Secret, csr, digestKey);
            Assert.Equal(EnrollmentClaimStatus.InProgress, retry.Status);
            Assert.Equal(winner.OperationId, retry.OperationId);
            Assert.Equal(winner.DeviceId, retry.DeviceId);
            var conflict = await reopened.ClaimAsync(pendingId, token.Secret, differentCsr, digestKey);
            Assert.Equal(EnrollmentClaimStatus.Conflict, conflict.Status);
            Assert.Null(conflict.DeviceId);
            Assert.Equal(EnrollmentClaimStatus.Unauthorized,
                (await reopened.ClaimAsync(pendingId, "invalid", differentCsr, digestKey)).Status);
            Assert.Equal(EnrollmentClaimStatus.Unauthorized,
                (await reopened.ClaimAsync(Guid.NewGuid(), token.Secret, csr, digestKey)).Status);

            var another = new EnrollmentBootstrapTokens(clock).Issue(Guid.NewGuid(), digestKey, TimeSpan.FromMinutes(5));
            await service.RegisterAsync(another.Credential);
            await Assert.ThrowsAsync<InvalidEnrollmentCsrException>(() => service.ClaimAsync(
                another.Credential.PendingEnrollmentId, another.Secret, "invalid", digestKey));
            await using (var context = factory.CreateDbContext())
            {
                var unchanged = await context.PendingEnrollments.SingleAsync(value => value.Id == another.Credential.PendingEnrollmentId);
                Assert.Null(unchanged.OperationId);
                Assert.Null(unchanged.DeviceId);
            }
            Assert.Equal(EnrollmentClaimStatus.Claimed, (await service.ClaimAsync(
                another.Credential.PendingEnrollmentId, another.Secret, csr, digestKey)).Status);

            clock.Now = token.Credential.ExpiresAt;
            Assert.Equal(EnrollmentClaimStatus.Unauthorized,
                (await reopened.ClaimAsync(pendingId, token.Secret, csr, digestKey)).Status);
            await using (var context = factory.CreateDbContext())
            {
                var persisted = await context.PendingEnrollments.SingleAsync(value => value.Id == pendingId);
                Assert.Equal(winner.OperationId, persisted.OperationId);
                Assert.Equal(winner.DeviceId, persisted.DeviceId);
                Assert.Equal(EnrollmentCsrVerifier.Verify(csr).CsrSha256, persisted.CsrSha256);
                Assert.Equal(token.Credential.ExpiresAt, persisted.ExpiresAt);
            }
        }
        catch (NpgsqlException)
        {
            throw new InvalidOperationException("PostgreSQL integration operation failed; connection details are redacted.");
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
                    throw new InvalidOperationException("PostgreSQL test-schema cleanup failed; connection details are redacted.");
                }
            }
        }
    }

    private static string CreateCsr()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(new CertificateRequest("CN=untrusted", key, HashAlgorithmName.SHA256).CreateSigningRequest());
    }

    private sealed class ContextFactory(DbContextOptions<FleetDbContext> options) : IDbContextFactory<FleetDbContext>
    {
        public FleetDbContext CreateDbContext() => new(options);
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}