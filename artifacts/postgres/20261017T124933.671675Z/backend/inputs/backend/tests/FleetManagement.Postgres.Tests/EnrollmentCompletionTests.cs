using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FleetManagement.Postgres.Tests;

public sealed class EnrollmentCompletionTests
{
    [Fact]
    public async Task CompletionIsValidatedFirstWriteWinsAndRecoveredOnlyWithOriginalAuthorization()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLEET_TEST_POSTGRES");
        Assert.False(string.IsNullOrWhiteSpace(connectionString), "An authorized disposable PostgreSQL database is required.");
        var schema = "fleet_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection))
            await create.ExecuteNonQueryAsync();
        try
        {
            var scoped = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
            var factory = new ContextFactory(new DbContextOptionsBuilder<FleetDbContext>().UseNpgsql(scoped.ConnectionString).Options);
            await using (var context = factory.CreateDbContext())
                await context.Database.MigrateAsync();
            var clock = new ManualClock();
            var service = new PostgresEnrollmentClaims(factory, clock);
            var digestKey = RandomNumberGenerator.GetBytes(32);
            var token = new EnrollmentBootstrapTokens(clock).Issue(Guid.NewGuid(), digestKey, TimeSpan.FromMinutes(5));
            await service.RegisterAsync(token.Credential);
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var csr = Convert.ToBase64String(new CertificateRequest("CN=ignored", key, HashAlgorithmName.SHA256).CreateSigningRequest());
            var claim = await service.ClaimAsync(token.Credential.PendingEnrollmentId, token.Secret, csr, digestKey);
            using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var rootRequest = new CertificateRequest("CN=Completion test root", rootKey, HashAlgorithmName.SHA256);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            using var root = rootRequest.CreateSelfSigned(clock.Now.AddDays(-1), clock.Now.AddDays(7));
            var request = new CertificateRequest("CN=server controlled", key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true));
            var names = new SubjectAlternativeNameBuilder();
            names.AddUri(new Uri(EnrollmentCertificateVerifier.DeviceUri(claim.DeviceId!.Value)));
            request.CertificateExtensions.Add(names.Build());
            using var leaf = request.Create(root, clock.Now.AddMinutes(-1), clock.Now.AddDays(1), RandomNumberGenerator.GetBytes(16));
            using var alternative = request.Create(root, clock.Now.AddMinutes(-1), clock.Now.AddHours(12), RandomNumberGenerator.GetBytes(16));
            var certificate = Convert.ToBase64String(leaf.RawData);
            var replacement = Convert.ToBase64String(alternative.RawData);
            var pendingId = token.Credential.PendingEnrollmentId;
            var operationId = claim.OperationId!.Value;
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(pendingId, Guid.NewGuid(),
                certificate, [], root, TimeSpan.FromDays(2)));
            await Assert.ThrowsAsync<InvalidEnrollmentCertificateException>(() => service.CompleteAsync(pendingId, operationId,
                "invalid", [], root, TimeSpan.FromDays(2)));
            await using (var context = factory.CreateDbContext())
                Assert.Null((await context.PendingEnrollments.SingleAsync()).CompletedResponseJson);
            var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => service.CompleteAsync(pendingId,
                operationId, index % 2 == 0 ? certificate : replacement, [], root, TimeSpan.FromDays(2))));
            var original = responses[0];
            Assert.All(responses, response =>
            {
                Assert.Equal(original.CertificateDer, response.CertificateDer);
                Assert.Equal(original.IssuedAt, response.IssuedAt);
                Assert.Equal(original.ExpiresAt, response.ExpiresAt);
            });
            var reopened = new PostgresEnrollmentClaims(factory, clock);
            var retry = await reopened.ClaimAsync(pendingId, token.Secret, csr, digestKey);
            Assert.Equal(EnrollmentClaimStatus.Completed, retry.Status);
            Assert.Equal(operationId, retry.OperationId);
            Assert.Equal(claim.DeviceId, retry.DeviceId);
            Assert.Equal(original.CertificateDer, retry.Response!.CertificateDer);
            Assert.Equal(original.ExpiresAt, retry.Response.ExpiresAt);
            Assert.Empty(retry.Response.IntermediateCertificatesDer);
            Assert.Equal(original.CertificateDer, (await reopened.CompleteAsync(pendingId, operationId,
                "not a new signing request", [], root, TimeSpan.FromDays(2))).CertificateDer);
            using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var differentCsr = Convert.ToBase64String(new CertificateRequest("CN=ignored", otherKey, HashAlgorithmName.SHA256).CreateSigningRequest());
            var conflict = await reopened.ClaimAsync(pendingId, token.Secret, differentCsr, digestKey);
            Assert.Equal(EnrollmentClaimStatus.Conflict, conflict.Status);
            Assert.Null(conflict.Response);
            var denied = await reopened.ClaimAsync(pendingId, "invalid", csr, digestKey);
            Assert.Equal(EnrollmentClaimStatus.Unauthorized, denied.Status);
            Assert.Null(denied.Response);
            await using (var context = factory.CreateDbContext())
            {
                var wrongIdentity = "{\"schemaVersion\":1,\"deviceId\":\"" + Guid.NewGuid().ToString("D") + "\"}";
                var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE \"PendingEnrollments\" SET \"CompletedResponseJson\" = {wrongIdentity} WHERE \"Id\" = {pendingId}"));
                Assert.Equal("CK_Enrollment_Completion", error.ConstraintName);
                Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            }
            clock.Now = token.Credential.ExpiresAt;
            var expired = await reopened.ClaimAsync(pendingId, token.Secret, csr, digestKey);
            Assert.Equal(EnrollmentClaimStatus.Unauthorized, expired.Status);
            Assert.Null(expired.Response);
            await using (var context = factory.CreateDbContext())
                Assert.Equal(token.Credential.ExpiresAt, (await context.PendingEnrollments.SingleAsync()).ExpiresAt);
        }
        catch (NpgsqlException)
        {
            throw new InvalidOperationException("PostgreSQL completion check failed; connection details are redacted.");
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await drop.ExecuteNonQueryAsync();
        }
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