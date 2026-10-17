using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using FleetManagement.Api;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

return await Run();

static async Task<int> Run()
{
    var schema = "fleet_test_" + Guid.NewGuid().ToString("N");
    var ledger = Path.Combine(Path.GetTempPath(), "fleet-android-" + Guid.NewGuid().ToString("N"));
    var connectionString = Environment.GetEnvironmentVariable("FLEET_TEST_POSTGRES");
    await using var connection = new NpgsqlConnection(connectionString);
    var schemaCreated = false;
    try
    {
        Ensure(!string.IsNullOrWhiteSpace(connectionString));
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection))
            await create.ExecuteNonQueryAsync();
        schemaCreated = true;
        var scoped = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
        var factory = new ContextFactory(new DbContextOptionsBuilder<FleetDbContext>().UseNpgsql(scoped.ConnectionString).Options);
        await using (var context = factory.CreateDbContext())
            await context.Database.MigrateAsync();
        var clock = TimeProvider.System;
        var now = clock.GetUtcNow();
        var digestKey = RandomNumberGenerator.GetBytes(32);
        using var deviceRootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var deviceRoot = Root("Android backend device root", deviceRootKey, now);
        using var serverRootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var serverRoot = Root("Android backend TLS root", serverRootKey, now);
        using var serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", serverKey, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Parse("10.0.2.2"));
        request.CertificateExtensions.Add(names.Build());
        using var serverPublic = request.Create(serverRoot, now.AddMinutes(-1), now.AddDays(1), RandomNumberGenerator.GetBytes(16));
        using var serverCertificate = serverPublic.CopyWithPrivateKey(serverKey);
        var claims = new PostgresEnrollmentClaims(factory, clock);
        var signer = new LabLedgerEnrollmentSigner(ledger, deviceRoot, clock, TimeSpan.FromDays(1));
        var coordinator = new EnrollmentSigningCoordinator(claims, signer, deviceRoot, TimeSpan.FromDays(2));
        var deviceAccess = new DisableAfterFirstIdentity(factory, deviceRoot, clock);
        using var operatorKey = RSA.Create(2048);
        var operatorSecurityKey = new RsaSecurityKey(operatorKey);
        await using var app = FleetApi.Build([], builder =>
        {
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listener =>
            {
                listener.Protocols = HttpProtocols.Http1;
                listener.UseHttps(serverCertificate, https => DeviceAuthentication.ConfigureHttps(https, deviceRoot, clock));
            }));
            builder.Services.AddSingleton<IEnrollmentRedemption>(new EnrollmentRedemptionService(claims, coordinator, digestKey));
            builder.Services.AddSingleton<IPendingEnrollmentProvisioner>(new PendingEnrollmentProvisioner(factory, clock, digestKey));
            builder.Services.AddSingleton<IDeviceCertificateIdentity>(deviceAccess);
            builder.Services.Configure<JwtBearerOptions>(AdministratorAuthentication.Scheme, options =>
            {
                options.TokenValidationParameters.ValidIssuer = "https://identity.example.test";
                options.TokenValidationParameters.ValidAudience = "fleet-api";
                options.TokenValidationParameters.IssuerSigningKey = operatorSecurityKey;
            });
        });
        await app.StartAsync();
        var address = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        var trust = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true
        };
        trust.CustomTrustStore.Add(serverRoot);
        using var handler = new SocketsHttpHandler { SslOptions = new SslClientAuthenticationOptions { CertificateChainPolicy = trust } };
        using var client = new HttpClient(handler) { BaseAddress = new Uri($"https://localhost:{address.Port}"), Timeout = TimeSpan.FromSeconds(20) };
        using (var denied = await client.PostAsJsonAsync("/api/v1/admin/pending-enrollments", new { schemaVersion = 1, lifetimeSeconds = 900 }))
            Ensure(denied.StatusCode == HttpStatusCode.Unauthorized);
        var access = new JwtSecurityToken("https://identity.example.test", "fleet-api",
            [new Claim("sub", "android-integration-operator"), new Claim("permission", "enrollments:create")],
            now.AddMinutes(-1).UtcDateTime, now.AddMinutes(5).UtcDateTime,
            new SigningCredentials(operatorSecurityKey, SecurityAlgorithms.RsaSha256));
        access.Header["typ"] = "at+jwt";
        using var provision = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/pending-enrollments")
        {
            Content = JsonContent.Create(new { schemaVersion = 1, lifetimeSeconds = 900 })
        };
        provision.Headers.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(access));
        using var provisioned = await client.SendAsync(provision);
        Ensure(provisioned.StatusCode == HttpStatusCode.Created);
        var issued = (await provisioned.Content.ReadFromJsonAsync<PendingEnrollmentProvisioned>())!;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schemaVersion = 1, stage = "ready", serverRootDer = Convert.ToBase64String(serverRoot.RawData),
            deviceRootDer = Convert.ToBase64String(deviceRoot.RawData), bootstrap = new
            {
                schemaVersion = 1, endpoint = $"https://10.0.2.2:{address.Port}/api/v1/enrollments",
                pendingEnrollmentId = issued.PendingEnrollmentId, bootstrapToken = issued.BootstrapToken
            }
        }));
        var proofLine = await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(4));
        Ensure(proofLine is { Length: > 0 and <= 200000 });
        using var proof = JsonDocument.Parse(proofLine!);
        var fields = proof.RootElement;
        Ensure(fields.GetProperty("mtlsAuthenticated").GetBoolean() && fields.GetProperty("disabledRejected").GetBoolean());
        Ensure(deviceAccess.Accepted == 1 && deviceAccess.Denied >= 1 && deviceAccess.AlternateCertificateRejected);
        var csr = fields.GetProperty("csrDer").GetString()!;
        var responseBytes = Convert.FromBase64String(fields.GetProperty("responseBase64").GetString()!);
        var response = JsonSerializer.Deserialize<EnrollmentCertificateResponse>(responseBytes, JsonSerializerOptions.Web)!;
        var verified = EnrollmentCsrVerifier.Verify(csr);
        EnrollmentCertificateVerifier.Verify(response.DeviceId, verified.SubjectPublicKeyInfoBase64,
            response.CertificateDer, response.IntermediateCertificatesDer, deviceRoot, clock.GetUtcNow(), TimeSpan.FromDays(2));
        Ensure(fields.GetProperty("deviceId").GetGuid() == response.DeviceId);
        using var leaf = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(response.CertificateDer));
        using var devicePublicKey = leaf.GetECDsaPublicKey()!;
        Ensure(devicePublicKey.VerifyData(Convert.FromBase64String(fields.GetProperty("challengeBase64").GetString()!),
            Convert.FromBase64String(fields.GetProperty("signatureBase64").GetString()!), HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence));
        await using (var reopened = factory.CreateDbContext())
        {
            var pending = await reopened.PendingEnrollments.SingleAsync();
            var audit = await reopened.EnrollmentProvisioningAudits.SingleAsync();
            Ensure(pending.Id == issued.PendingEnrollmentId && pending.DeviceId == response.DeviceId);
            Ensure(pending.DeviceAccessDisabled);
            Ensure(pending.CompletedResponseJson is not null && Encoding.UTF8.GetBytes(pending.CompletedResponseJson).SequenceEqual(responseBytes));
            Ensure(audit.PendingEnrollmentId == pending.Id && audit.Subject == "android-integration-operator");
            Ensure(!JsonSerializer.Serialize(pending).Contains(issued.BootstrapToken, StringComparison.Ordinal));
        }
        Ensure(Directory.GetFiles(ledger, "*.issued.json").Length == 1);
        using var spoofed = new HttpRequestMessage(HttpMethod.Get, "/api/v1/device/identity");
        spoofed.Headers.TryAddWithoutValidation("X-ARR-ClientCert", response.CertificateDer);
        using var anonymous = await client.SendAsync(spoofed);
        Ensure(anonymous.StatusCode == HttpStatusCode.Unauthorized);
        await app.StopAsync();
        Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, stage = "verified", deviceId = response.DeviceId,
            pendingEnrollmentId = issued.PendingEnrollmentId, issuedCertificates = 1, provisioningAudits = 1,
            publicKeyProof = true, exactResponseReplay = true, mtlsAuthenticated = true,
            disabledRejected = true, alternateCertificateRejected = true, forwardedCertificateRejected = true }));
        return 0;
    }
    catch (Exception)
    {
        Console.Error.WriteLine("Android/backend integration failed; private inputs and exception details are redacted.");
        return 1;
    }
    finally
    {
        if (schemaCreated)
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection);
            await drop.ExecuteNonQueryAsync();
        }
        if (Directory.Exists(ledger)) Directory.Delete(ledger, recursive: true);
    }
}

static X509Certificate2 Root(string name, ECDsa key, DateTimeOffset now)
{
    var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
    request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
    request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
    return request.CreateSelfSigned(now.AddDays(-1), now.AddDays(7));
}

static void Ensure(bool condition)
{
    if (!condition) throw new InvalidOperationException("Integration assertion failed.");
}

sealed class ContextFactory(DbContextOptions<FleetDbContext> options) : IDbContextFactory<FleetDbContext>
{
    public FleetDbContext CreateDbContext() => new(options);
}

sealed class DisableAfterFirstIdentity(ContextFactory factory, X509Certificate2 root, TimeProvider clock) : IDeviceCertificateIdentity
{
    private readonly DeviceCertificateIdentity identity = new(factory, root, clock);
    public int Accepted { get; private set; }
    public int Denied { get; private set; }
    public bool AlternateCertificateRejected { get; private set; }

    public async Task<Guid?> AuthenticateAsync(X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        var deviceId = await identity.AuthenticateAsync(certificate, cancellationToken);
        if (deviceId is null)
        {
            Denied++;
            return null;
        }
        Accepted++;
        var now = clock.GetUtcNow();
        var request = new CertificateRequest(new X500DistinguishedName("CN=Unregistered alternate certificate"),
            certificate.PublicKey, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddUri(new Uri(EnrollmentCertificateVerifier.DeviceUri(deviceId.Value)));
        request.CertificateExtensions.Add(names.Build());
        using var alternate = request.Create(root, now.AddMinutes(-1), now.AddDays(1), RandomNumberGenerator.GetBytes(16));
        DeviceCertificateProof.Verify(alternate, [], root, now);
        AlternateCertificateRejected = await identity.AuthenticateAsync(alternate, cancellationToken) is null;
        EnsureValid(AlternateCertificateRejected);
        await using var context = factory.CreateDbContext();
        var enrollment = await context.PendingEnrollments.SingleAsync(value => value.DeviceId == deviceId, cancellationToken);
        enrollment.DisableDeviceAccess();
        await context.SaveChangesAsync(cancellationToken);
        return deviceId;
    }

    private static void EnsureValid(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Alternate certificate was not rejected.");
    }
}