using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FleetManagement.Enrollment;
using FleetManagement.Messaging;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FleetManagement.Postgres.Tests;

public sealed class CommandEventIngestionTests
{
    [Fact]
    public async Task OwnershipConcurrentDeduplicationRollbackAndDisabledRetriesUseCommittedState()
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
            var deviceId = await Enroll(factory, clock);
            var otherDevice = await Enroll(factory, clock);
            var commandId = Guid.NewGuid();
            var correlationId = Guid.NewGuid();
            var eventId = Guid.NewGuid();
            var service = new PostgresCommandEvents(factory, clock);
            var body = Message(deviceId, commandId, correlationId, eventId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.IngestAsync(body, deviceId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterOwnershipAsync(commandId, Guid.NewGuid(), correlationId));
            await Assert.ThrowsAsync<ArgumentException>(() => service.RegisterOwnershipAsync(Guid.Empty, deviceId, correlationId));
            await service.RegisterOwnershipAsync(commandId, deviceId, correlationId);
            await service.RegisterOwnershipAsync(commandId, deviceId, correlationId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterOwnershipAsync(commandId, otherDevice, correlationId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RegisterOwnershipAsync(commandId, deviceId, Guid.NewGuid()));
            await Assert.ThrowsAsync<ArgumentException>(() => service.IngestAsync(body, otherDevice));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.IngestAsync(
                Message(otherDevice, commandId, correlationId, Guid.NewGuid()), otherDevice));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.IngestAsync(
                Message(deviceId, commandId, Guid.NewGuid(), Guid.NewGuid()), deviceId));

            var contenders = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.IngestAsync(body, deviceId)));
            var original = Assert.Single(contenders, value => !value.Duplicate);
            Assert.Equal(7, contenders.Count(value => value.Duplicate));
            Assert.All(contenders, value => Assert.Equal(original.AcknowledgementJson, value.AcknowledgementJson));
            using (var acknowledgement = JsonDocument.Parse(original.AcknowledgementJson))
            {
                Assert.Equal(4, acknowledgement.RootElement.EnumerateObject().Count());
                Assert.Equal(1, acknowledgement.RootElement.GetProperty("schemaVersion").GetInt32());
                Assert.Equal(deviceId, acknowledgement.RootElement.GetProperty("deviceId").GetGuid());
                Assert.Equal(eventId, acknowledgement.RootElement.GetProperty("eventId").GetGuid());
                Assert.Equal(clock.Now, acknowledgement.RootElement.GetProperty("receivedAt").GetDateTimeOffset());
            }
            clock.Now = clock.Now.AddHours(1);
            var reopened = new PostgresCommandEvents(factory, clock);
            var retry = await reopened.IngestAsync(body, deviceId);
            Assert.True(retry.Duplicate);
            Assert.Equal(original.AcknowledgementJson, retry.AcknowledgementJson);
            var equivalent = Encoding.UTF8.GetString(body).Replace("\"schemaVersion\":1", "\"schemaVersion\":1.0");
            Assert.Equal(original.AcknowledgementJson, (await reopened.IngestAsync(Encoding.UTF8.GetBytes(equivalent), deviceId)).AcknowledgementJson);
            var changed = Encoding.UTF8.GetString(body).Replace("received", "running");
            await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.IngestAsync(Encoding.UTF8.GetBytes(changed), deviceId));
            var changedTime = Encoding.UTF8.GetString(body).Replace("12:00:00Z", "14:00:00+02:00");
            await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.IngestAsync(Encoding.UTF8.GetBytes(changedTime), deviceId));

            var failedId = Guid.NewGuid();
            await using (var context = factory.CreateDbContext())
            {
                Assert.Equal(1, await context.StoredCommandEvents.CountAsync());
                Assert.Equal(1, await context.CommandEventOwnerships.CountAsync());
                await context.Database.ExecuteSqlRawAsync("""
                    CREATE FUNCTION reject_test_event() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN RAISE EXCEPTION 'test event insertion failure'; END $$;
                    CREATE TRIGGER reject_test_event BEFORE INSERT ON "StoredCommandEvents"
                    FOR EACH ROW EXECUTE FUNCTION reject_test_event();
                    """);
            }
            await Assert.ThrowsAsync<DbUpdateException>(() => reopened.IngestAsync(Message(deviceId, commandId, correlationId, failedId), deviceId));
            await using (var context = factory.CreateDbContext())
            {
                Assert.Equal(1, await context.StoredCommandEvents.CountAsync());
                await context.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_test_event ON \"StoredCommandEvents\"; DROP FUNCTION reject_test_event();");
            }
            var recovered = await reopened.IngestAsync(Message(deviceId, commandId, correlationId, failedId), deviceId);
            Assert.False(recovered.Duplicate);
            var otherCommand = Guid.NewGuid();
            var otherCorrelation = Guid.NewGuid();
            await reopened.RegisterOwnershipAsync(otherCommand, otherDevice, otherCorrelation);
            await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.IngestAsync(
                Message(otherDevice, otherCommand, otherCorrelation, eventId), otherDevice));
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reopened.IngestAsync(
                    Message(deviceId, commandId, correlationId, Guid.NewGuid()), deviceId, cancelled.Token));
            }
            await using (var context = factory.CreateDbContext())
            {
                var violation = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE \"StoredCommandEvents\" SET \"CorrelationId\" = {Guid.NewGuid()} WHERE \"EventId\" = {eventId}"));
                Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, violation.SqlState);
                Assert.Equal(2, await context.StoredCommandEvents.CountAsync());
                var enrollment = await context.PendingEnrollments.SingleAsync(value => value.DeviceId == deviceId);
                enrollment.DisableDeviceAccess();
                await context.SaveChangesAsync();
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.IngestAsync(body, deviceId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.IngestAsync(
                Message(deviceId, commandId, correlationId, Guid.NewGuid()), deviceId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.RegisterOwnershipAsync(Guid.NewGuid(), deviceId, Guid.NewGuid()));
            await using (var context = factory.CreateDbContext())
                Assert.Equal(2, await context.StoredCommandEvents.CountAsync());
        }
        catch (NpgsqlException)
        {
            throw new InvalidOperationException("Command event PostgreSQL check failed; connection details are redacted.");
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static byte[] Message(Guid deviceId, Guid commandId, Guid correlationId, Guid eventId) => Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { schemaVersion = 1, eventId, commandId, deviceId, correlationId,
            recordedAt = "2026-10-17T12:00:00Z", state = "received" }));

    private static async Task<Guid> Enroll(ContextFactory factory, ManualClock clock)
    {
        var service = new PostgresEnrollmentClaims(factory, clock);
        var digestKey = RandomNumberGenerator.GetBytes(32);
        var token = new EnrollmentBootstrapTokens(clock).Issue(Guid.NewGuid(), digestKey, TimeSpan.FromMinutes(5));
        await service.RegisterAsync(token.Credential);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=ignored", key, HashAlgorithmName.SHA256);
        var claim = await service.ClaimAsync(token.Credential.PendingEnrollmentId, token.Secret,
            Convert.ToBase64String(request.CreateSigningRequest()), digestKey);
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = new CertificateRequest("CN=Event test root", rootKey, HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var root = rootRequest.CreateSelfSigned(clock.Now.AddDays(-1), clock.Now.AddDays(7));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddUri(new Uri(EnrollmentCertificateVerifier.DeviceUri(claim.DeviceId!.Value)));
        request.CertificateExtensions.Add(names.Build());
        using var leaf = request.Create(root, clock.Now.AddMinutes(-1), clock.Now.AddDays(1), RandomNumberGenerator.GetBytes(16));
        await service.CompleteAsync(token.Credential.PendingEnrollmentId, claim.OperationId!.Value,
            Convert.ToBase64String(leaf.RawData), [], root, TimeSpan.FromDays(2));
        return claim.DeviceId.Value;
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