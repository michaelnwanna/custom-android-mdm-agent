using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using FleetManagement.Policies;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FleetManagement.Postgres.Tests;

public sealed class PolicyPersistenceTests
{
    private sealed class ContextFactory(DbContextOptions<FleetDbContext> options) : IDbContextFactory<FleetDbContext>
    {
        public FleetDbContext CreateDbContext() => new(options);
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 18, 13, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task PolicyCreationIsIdempotent()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLEET_TEST_POSTGRES");
        Assert.True(!string.IsNullOrWhiteSpace(connectionString), 
            "Set FLEET_TEST_POSTGRES to an authorized disposable PostgreSQL database");
        
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

            await using var context1 = factory.CreateDbContext();
            var service = new PolicyService(context1);
            
            // Create policy twice with identical JSON
            const string policyJson = "{\"version\":1,\"settings\":{\"restrictedApps\":[]}}";
            var policyId1 = await service.CreatePolicyIdempotentAsync(policyJson, "admin@example.com");
            var policyId2 = await service.CreatePolicyIdempotentAsync(policyJson, "admin@example.com");
            
            // Must produce identical IDs
            Assert.Equal(policyId1, policyId2);
            
            // Only one policy should exist in database
            await using (var context = factory.CreateDbContext())
            {
                var policies = await context.Set<Policy>().ToListAsync();
                Assert.Single(policies);
                Assert.Equal(policyId1, policies[0].PolicyId);
            }
        }
        finally
        {
            if (created)
                try { await using var connection = new NpgsqlConnection(connectionString);
                    await connection.OpenAsync();
                    await using (var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection))
                        await drop.ExecuteNonQueryAsync();
                } catch { }
        }
    }

    [Fact]
    public async Task PolicyAssignmentTracksVersions()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLEET_TEST_POSTGRES");
        Assert.True(!string.IsNullOrWhiteSpace(connectionString),
            "Set FLEET_TEST_POSTGRES to an authorized disposable PostgreSQL database");

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

            // Setup enrollment using bootstrap pattern
            var clock = new ManualClock();
            var digestKey = RandomNumberGenerator.GetBytes(32);
            var pendingId = Guid.NewGuid();
            var token = new EnrollmentBootstrapTokens(clock).Issue(pendingId, digestKey, TimeSpan.FromMinutes(5));
            var enrollmentService = new PostgresEnrollmentClaims(factory, clock);
            await enrollmentService.RegisterAsync(token.Credential);
            
            // Claim enrollment to get a device ID
            var csr = CreateTestCsr();
            var claim = await enrollmentService.ClaimAsync(pendingId, token.Secret, csr, digestKey);
            Assert.Equal(EnrollmentClaimStatus.Claimed, claim.Status);
            var enrollmentId = pendingId;

            await using var context1 = factory.CreateDbContext();
            var policyService = new PolicyService(context1);
            
            // Create and assign policy
            const string policyJson = "{\"version\":1}";
            var policyId = await policyService.CreatePolicyIdempotentAsync(policyJson, "admin");
            var assignmentId = await policyService.AssignPolicyAsync(enrollmentId, policyId);
            
            // Verify assignment state
            var assignment = await policyService.GetAssignmentAsync(assignmentId);
            Assert.NotNull(assignment);
            Assert.Equal(policyId, assignment!.PolicyId);
            Assert.Equal(enrollmentId, assignment.EnrollmentId);
            Assert.Equal(1L, assignment.AssignedVersion);
            Assert.Null(assignment.AcknowledgedVersion);
        }
        finally
        {
            if (created)
                try { await using var connection = new NpgsqlConnection(connectionString);
                    await connection.OpenAsync();
                    await using (var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection))
                        await drop.ExecuteNonQueryAsync();
                } catch { }
        }
    }

    [Fact]
    public async Task PolicyCommandGenerationWithRealDatabase()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLEET_TEST_POSTGRES");
        Assert.True(!string.IsNullOrWhiteSpace(connectionString),
            "Set FLEET_TEST_POSTGRES to an authorized disposable PostgreSQL database");

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

            // Setup enrolled device
            var clock = new ManualClock();
            var digestKey = RandomNumberGenerator.GetBytes(32);
            var pendingId = Guid.NewGuid();
            var token = new EnrollmentBootstrapTokens(clock).Issue(pendingId, digestKey, TimeSpan.FromMinutes(5));
            var enrollmentService = new PostgresEnrollmentClaims(factory, clock);
            await enrollmentService.RegisterAsync(token.Credential);
            
            var csr = CreateTestCsr();
            var claim = await enrollmentService.ClaimAsync(pendingId, token.Secret, csr, digestKey);
            Assert.Equal(EnrollmentClaimStatus.Claimed, claim.Status);
            var enrollmentId = pendingId;

            await using var context1 = factory.CreateDbContext();
            var policyService = new PolicyService(context1);
            var generator = new PolicyCommandGenerator(factory, clock);

            // Create, assign, and generate commands
            const string policyJson = "{\"version\":1}";
            var policyId = await policyService.CreatePolicyIdempotentAsync(policyJson, "admin");
            var assignmentId = await policyService.AssignPolicyAsync(enrollmentId, policyId);

            // Generate commands for active assignments
            int generated = await generator.GenerateActiveAssignmentsAsync();
            Assert.True(generated > 0, "Should generate at least one command");

            // Re-run generation (idempotent check)
            int generated2 = await generator.GenerateActiveAssignmentsAsync();
            Assert.Equal(0, generated2); // No new commands on retry (command already registered)
        }
        finally
        {
            if (created)
                try { await using var connection = new NpgsqlConnection(connectionString);
                    await connection.OpenAsync();
                    await using (var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection))
                        await drop.ExecuteNonQueryAsync();
                } catch { }
        }
    }

    private static string CreateTestCsr()
    {
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(new CertificateRequest("CN=test", key, HashAlgorithmName.SHA256).CreateSigningRequest());
    }
}
