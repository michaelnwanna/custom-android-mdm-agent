using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using FleetManagement.Enrollment;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Options;

namespace FleetManagement.Api;

public static class DeviceAuthentication
{
    public const string Scheme = "device-certificate";
    public const string Policy = "enrolled-device";

    public static void AddDeviceAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, DeviceCertificateHandler>(Scheme, _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy(Policy, policy =>
            policy.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser().RequireClaim("device_id")));
    }

    public static void ConfigureHttps(HttpsConnectionAdapterOptions options, X509Certificate2 trustedRoot, TimeProvider clock)
    {
        options.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
        options.OnAuthenticate = (_, tls) =>
        {
            var policy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = X509RevocationMode.NoCheck,
                DisableCertificateDownloads = true
            };
            policy.CustomTrustStore.Add(trustedRoot);
            policy.ApplicationPolicy.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.2"));
            tls.CertificateChainPolicy = policy;
        };
        options.ClientCertificateValidation = (certificate, chain, errors) =>
        {
            if (errors != System.Net.Security.SslPolicyErrors.None || chain is null)
                return false;
            try
            {
                var intermediates = chain.ChainElements.Cast<X509ChainElement>().Skip(1)
                    .Where(element => !element.Certificate.RawData.AsSpan().SequenceEqual(trustedRoot.RawData))
                    .Select(element => Convert.ToBase64String(element.Certificate.RawData)).ToArray();
                DeviceCertificateProof.Verify(certificate, intermediates, trustedRoot, clock.GetUtcNow());
                return true;
            }
            catch (InvalidEnrollmentCertificateException)
            {
                return false;
            }
        };
    }

    public static void MapDeviceIdentity(this WebApplication app)
    {
        app.MapGet("/api/v1/device/identity", (ClaimsPrincipal user) =>
            Results.Ok(new DeviceIdentityResponse(1, Guid.Parse(user.FindFirst("device_id")!.Value))))
            .WithName("GetDeviceIdentity").RequireAuthorization(Policy).RequireRateLimiting("device")
            .Produces<DeviceIdentityResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .ProducesProblem(429).ProducesProblem(500).ProducesProblem(503);
    }
}

public sealed record DeviceIdentityResponse(int SchemaVersion, Guid DeviceId);

public sealed class DeviceCertificateHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.IsHttps)
            return AuthenticateResult.NoResult();
        var certificate = await Context.Connection.GetClientCertificateAsync(Context.RequestAborted);
        if (certificate is null)
            return AuthenticateResult.NoResult();
        var service = Context.RequestServices.GetService<IDeviceCertificateIdentity>();
        if (service is null)
            return AuthenticateResult.Fail("Device authentication unavailable.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Context.RequestAborted);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        Guid? deviceId;
        try
        {
            deviceId = await service.AuthenticateAsync(certificate, deadline.Token);
        }
        catch (OperationCanceledException) when (!Context.RequestAborted.IsCancellationRequested)
        {
            throw new IOException("Device authentication unavailable.");
        }
        if (deviceId is null || deviceId == Guid.Empty)
            return AuthenticateResult.Fail("Device certificate rejected.");
        var identity = new ClaimsIdentity([new Claim("device_id", deviceId.Value.ToString("D"))], DeviceAuthentication.Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), DeviceAuthentication.Scheme));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) => FleetApi.Problem(401).ExecuteAsync(Context);
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) => FleetApi.Problem(403).ExecuteAsync(Context);
}