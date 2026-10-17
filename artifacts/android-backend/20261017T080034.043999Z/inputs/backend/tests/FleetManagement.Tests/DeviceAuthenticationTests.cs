using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FleetManagement.Api;
using FleetManagement.Enrollment;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FleetManagement.Tests;

public sealed class DeviceAuthenticationTests
{
    [Fact]
    public async Task UnconfiguredDeviceRouteRejectsBearerAndForwardedCertificateHeaders()
    {
        await using var app = FleetApi.Build([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/v1/device/identity");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + new string('A', 43));
        request.Headers.TryAddWithoutValidation("X-ARR-ClientCert", "untrusted-header");
        request.Headers.TryAddWithoutValidation("X-SSL-CERT", "untrusted-header");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task RealTlsUsesCertificateProofAndDoesNotGrantAdministratorAccess()
    {
        var now = DateTimeOffset.UtcNow;
        var deviceId = Guid.NewGuid();
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = new CertificateRequest("CN=mTLS test root", rootKey, HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var root = rootRequest.CreateSelfSigned(now.AddDays(-1), now.AddDays(3));
        using var serverRootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var serverRootRequest = new CertificateRequest("CN=Separate server test root", serverRootKey, HashAlgorithmName.SHA256);
        serverRootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        serverRootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var serverRoot = serverRootRequest.CreateSelfSigned(now.AddDays(-1), now.AddDays(3));
        using var serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var server = Leaf(serverKey, serverRoot, now, null);
        using var clientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var certificate = Leaf(clientKey, root, now, deviceId);
        var service = new IdentityStub(deviceId, certificate.RawData);
        await using var app = FleetApi.Build([], builder =>
        {
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0,
                listener => listener.UseHttps(server, https => DeviceAuthentication.ConfigureHttps(https, root, TimeProvider.System))));
            builder.Services.AddSingleton<IDeviceCertificateIdentity>(service);
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var endpoint = new UriBuilder(address) { Host = "localhost" }.Uri;
        using var anonymous = Client(endpoint, serverRoot, null);
        using var denied = await anonymous.GetAsync("/api/v1/device/identity");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var client = Client(endpoint, serverRoot, certificate);
        using var accepted = await client.GetAsync("/api/v1/device/identity");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var body = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        Assert.Equal(deviceId, body.RootElement.GetProperty("deviceId").GetGuid());
        using var admin = await client.PostAsync("/api/v1/admin/pending-enrollments", null);
        Assert.Equal(HttpStatusCode.Unauthorized, admin.StatusCode);
        service.Enabled = false;
        using var disabled = await client.GetAsync("/api/v1/device/identity");
        Assert.Equal(HttpStatusCode.Unauthorized, disabled.StatusCode);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherCertificate = Leaf(otherKey, root, now, deviceId);
        using var other = Client(endpoint, serverRoot, otherCertificate);
        using var unregistered = await other.GetAsync("/api/v1/device/identity");
        Assert.Equal(HttpStatusCode.Unauthorized, unregistered.StatusCode);
    }

    private static X509Certificate2 Leaf(ECDsa key, X509Certificate2 root, DateTimeOffset now, Guid? deviceId)
    {
        var request = new CertificateRequest("CN=TLS test leaf", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
            { new(deviceId is null ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, true));
        var names = new SubjectAlternativeNameBuilder();
        if (deviceId is { } identity) names.AddUri(new Uri(EnrollmentCertificateVerifier.DeviceUri(identity)));
        else names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.Create(root, now.AddMinutes(-1), now.AddDays(1), RandomNumberGenerator.GetBytes(16));
        return certificate.CopyWithPrivateKey(key);
    }

    private static HttpClient Client(Uri endpoint, X509Certificate2 root, X509Certificate2? certificate)
    {
        var policy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true
        };
        policy.CustomTrustStore.Add(root);
        var tls = new SslClientAuthenticationOptions { CertificateChainPolicy = policy };
        if (certificate is not null) tls.ClientCertificates = new X509CertificateCollection { certificate };
        return new HttpClient(new SocketsHttpHandler { SslOptions = tls }) { BaseAddress = endpoint };
    }

    private sealed class IdentityStub(Guid deviceId, byte[] expected) : IDeviceCertificateIdentity
    {
        public bool Enabled { get; set; } = true;
        public Task<Guid?> AuthenticateAsync(X509Certificate2 certificate, CancellationToken cancellationToken)
            => Task.FromResult<Guid?>(Enabled && certificate.RawData.AsSpan().SequenceEqual(expected) ? deviceId : null);
    }
}