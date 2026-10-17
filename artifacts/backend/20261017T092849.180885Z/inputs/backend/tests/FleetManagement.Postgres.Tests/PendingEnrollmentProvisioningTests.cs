using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using FleetManagement.Api;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace FleetManagement.Postgres.Tests;

public sealed class PendingEnrollmentProvisioningTests
{
    [Fact]
    public async Task AuthorizedHttpCreationCommitsDigestAndAuditTogetherAndAuditFailureRollsBack()
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
            var digestKey = RandomNumberGenerator.GetBytes(32);
            var service = new PendingEnrollmentProvisioner(factory, TimeProvider.System, digestKey);
            using var key = RSA.Create(2048);
            var securityKey = new RsaSecurityKey(key);
            await using var app = FleetApi.Build([], builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Logging.ClearProviders();
                builder.Services.AddSingleton<IPendingEnrollmentProvisioner>(service);
                builder.Services.Configure<JwtBearerOptions>(AdministratorAuthentication.Scheme, options =>
                {
                    options.TokenValidationParameters.ValidIssuer = "https://identity.example.test";
                    options.TokenValidationParameters.ValidAudience = "fleet-api";
                    options.TokenValidationParameters.IssuerSigningKey = securityKey;
                });
            });
            await app.StartAsync();
            using var client = app.GetTestClient();
            var token = new JwtSecurityToken("https://identity.example.test", "fleet-api",
                [new Claim("sub", "operator"), new Claim("permission", "enrollments:create")],
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5),
                new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256));
            token.Header["typ"] = "at+jwt";
            var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

            async Task<HttpResponseMessage> Send(bool authorized)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/api/v1/admin/pending-enrollments")
                {
                    Content = JsonContent.Create(new { schemaVersion = 1, lifetimeSeconds = 300 })
                };
                if (authorized)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                return await client.SendAsync(request);
            }

            using var unauthorized = await Send(false);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
            await using (var empty = factory.CreateDbContext())
            {
                Assert.Empty(await empty.PendingEnrollments.ToListAsync());
                Assert.Empty(await empty.EnrollmentProvisioningAudits.ToListAsync());
            }
            using var response = await Send(true);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            var issued = (await response.Content.ReadFromJsonAsync<PendingEnrollmentProvisioned>())!;
            Assert.NotEqual(Guid.Empty, issued.PendingEnrollmentId);
            Assert.Equal(43, issued.BootstrapToken.Length);
            Assert.Equal(TimeSpan.FromMinutes(5), issued.ExpiresAt - issued.IssuedAt);
            await using (var reopened = factory.CreateDbContext())
            {
                var pending = await reopened.PendingEnrollments.SingleAsync();
                var audit = await reopened.EnrollmentProvisioningAudits.SingleAsync();
                Assert.Equal(issued.PendingEnrollmentId, pending.Id);
                Assert.True(new EnrollmentBootstrapTokens(TimeProvider.System).Verify(pending.Id,
                    issued.BootstrapToken, pending.Credential, digestKey));
                Assert.DoesNotContain(issued.BootstrapToken, JsonSerializer.Serialize(pending));
                Assert.Equal(new EnrollmentProvisioningAudit(pending.Id, "https://identity.example.test", "operator", issued.IssuedAt), audit);
                await reopened.Database.ExecuteSqlRawAsync("""
                    CREATE FUNCTION reject_provisioning_audit() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN RAISE EXCEPTION 'forced audit failure'; END; $$;
                    CREATE TRIGGER reject_provisioning BEFORE INSERT ON "EnrollmentProvisioningAudits"
                    FOR EACH ROW EXECUTE FUNCTION reject_provisioning_audit();
                    """);
            }
            using var failed = await Send(true);
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            var error = await failed.Content.ReadAsStringAsync();
            Assert.DoesNotContain("forced audit failure", error);
            Assert.DoesNotContain(issued.BootstrapToken, error);
            await using var afterFailure = factory.CreateDbContext();
            Assert.Single(await afterFailure.PendingEnrollments.ToListAsync());
            Assert.Single(await afterFailure.EnrollmentProvisioningAudits.ToListAsync());
        }
        catch (NpgsqlException)
        {
            throw new InvalidOperationException("PostgreSQL provisioning check failed; connection details are redacted.");
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
}