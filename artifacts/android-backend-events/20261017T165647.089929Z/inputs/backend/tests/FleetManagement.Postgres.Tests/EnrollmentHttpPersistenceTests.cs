using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FleetManagement.Api;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace FleetManagement.Postgres.Tests;

public sealed class EnrollmentHttpPersistenceTests
{
    [Fact]
    public async Task HttpEnrollmentPersistsOneCertificateAndRecoversExactBytesWithOriginalAuthorization()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLEET_TEST_POSTGRES");
        Assert.False(string.IsNullOrWhiteSpace(connectionString), "An authorized disposable PostgreSQL database is required.");
        var schema = "fleet_test_" + Guid.NewGuid().ToString("N");
        var ledger = Path.Combine(Path.GetTempPath(), "fleet-http-" + Guid.NewGuid().ToString("N"));
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
            var claims = new PostgresEnrollmentClaims(factory, clock);
            var digestKey = RandomNumberGenerator.GetBytes(32);
            var token = new EnrollmentBootstrapTokens(clock).Issue(Guid.NewGuid(), digestKey, TimeSpan.FromMinutes(5));
            await claims.RegisterAsync(token.Credential);
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var csr = Convert.ToBase64String(new CertificateRequest("CN=untrusted", key, HashAlgorithmName.SHA256).CreateSigningRequest());
            using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var rootRequest = new CertificateRequest("CN=HTTP test root", rootKey, HashAlgorithmName.SHA256);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            using var root = rootRequest.CreateSelfSigned(clock.Now.AddDays(-1), clock.Now.AddDays(7));

            EnrollmentRedemptionService Service()
            {
                var reopenedClaims = new PostgresEnrollmentClaims(factory, clock);
                var signer = new LabLedgerEnrollmentSigner(ledger, root, clock, TimeSpan.FromDays(1));
                return new EnrollmentRedemptionService(reopenedClaims,
                    new EnrollmentSigningCoordinator(reopenedClaims, signer, root, TimeSpan.FromDays(2)), digestKey);
            }

            await using var app = FleetApi.Build([], builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Logging.ClearProviders();
                builder.Services.AddSingleton<IEnrollmentRedemption>(Service());
            });
            await app.StartAsync();
            using var client = app.GetTestClient();

            async Task<HttpResponseMessage> Send(string secret, string signingRequest, Guid? pendingId = null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/api/v1/enrollments")
                {
                    Content = JsonContent.Create(new
                    {
                        schemaVersion = 1, pendingEnrollmentId = pendingId ?? token.Credential.PendingEnrollmentId,
                        csrDer = signingRequest
                    })
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                return await client.SendAsync(request);
            }

            using var first = await Send(token.Secret, csr);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            var originalBytes = await first.Content.ReadAsByteArrayAsync();
            var response = JsonSerializer.Deserialize<EnrollmentCertificateResponse>(originalBytes, JsonSerializerOptions.Web)!;
            var verifiedKey = EnrollmentCsrVerifier.Verify(csr);
            EnrollmentCertificateVerifier.Verify(response.DeviceId, verifiedKey.SubjectPublicKeyInfoBase64,
                response.CertificateDer, response.IntermediateCertificatesDer, root, clock.Now, TimeSpan.FromDays(2));
            var retries = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Send(token.Secret, csr)));
            foreach (var retry in retries)
            {
                using (retry)
                {
                    Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
                    Assert.Equal(originalBytes, await retry.Content.ReadAsByteArrayAsync());
                }
            }
            var reopened = await Service().RedeemAsync(new EnrollmentHttpRequest(token.Credential.PendingEnrollmentId, csr), token.Secret, default);
            Assert.Equal(EnrollmentRedemptionStatus.Recovered, reopened.Status);
            Assert.Equal(originalBytes, JsonSerializer.SerializeToUtf8Bytes(reopened.Response, JsonSerializerOptions.Web));
            Assert.Single(Directory.GetFiles(ledger, "*.issued.json"));
            using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var otherCsr = Convert.ToBase64String(new CertificateRequest("CN=other", otherKey, HashAlgorithmName.SHA256).CreateSigningRequest());
            using var conflict = await Send(token.Secret, otherCsr);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            using var invalid = await Send(new string('A', 43), csr);
            using var unknown = await Send(token.Secret, csr, Guid.NewGuid());
            Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
            Assert.Equal(await invalid.Content.ReadAsByteArrayAsync(), await unknown.Content.ReadAsByteArrayAsync());
            clock.Now = token.Credential.ExpiresAt;
            using var expired = await Send(token.Secret, csr);
            Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
            await using (var context = factory.CreateDbContext())
            {
                var pending = await context.PendingEnrollments.SingleAsync();
                Assert.Equal(token.Credential.ExpiresAt, pending.ExpiresAt);
                Assert.Equal(response.DeviceId, pending.DeviceId);
                Assert.NotNull(pending.CompletedResponseJson);
            }
        }
        catch (NpgsqlException)
        {
            throw new InvalidOperationException("PostgreSQL HTTP check failed; connection details are redacted.");
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await drop.ExecuteNonQueryAsync();
            if (Directory.Exists(ledger))
                Directory.Delete(ledger, recursive: true);
        }
    }

    private sealed class ContextFactory(DbContextOptions<FleetDbContext> options) : IDbContextFactory<FleetDbContext>
    {
        public FleetDbContext CreateDbContext() => new(options);
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}