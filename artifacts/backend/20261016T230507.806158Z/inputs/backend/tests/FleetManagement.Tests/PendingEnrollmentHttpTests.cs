using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FleetManagement.Api;
using FleetManagement.Enrollment;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FleetManagement.Tests;

public sealed class PendingEnrollmentHttpTests
{
    private const string ValidBody = "{\"schemaVersion\":1,\"lifetimeSeconds\":300}";

    [Theory]
    [InlineData("valid", 201)]
    [InlineData("missing", 401)]
    [InlineData("forbidden", 403)]
    [InlineData("disabled", 503)]
    [InlineData("failure", 503)]
    public async Task OnlyAuthorizedCommittedProvisioningReturnsSecret(string scenario, int expected)
    {
        var provisioner = new ProvisionerStub { Fail = scenario == "failure" };
        var (app, token) = Start(provisioner, scenario != "disabled", scenario != "forbidden");
        await using var host = app;
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var request = Request(ValidBody, scenario == "missing" ? null : token);
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var text = await response.Content.ReadAsStringAsync();
        if (expected == 201)
        {
            using var document = JsonDocument.Parse(text);
            Assert.Equal(provisioner.Response.BootstrapToken, document.RootElement.GetProperty("bootstrapToken").GetString());
            Assert.Equal(new EnrollmentAdministrator("https://identity.example.test", "operator"), provisioner.Administrator);
            Assert.Equal(TimeSpan.FromMinutes(5), provisioner.Lifetime);
            Assert.DoesNotContain(provisioner.Response.BootstrapToken, provisioner.Response.ToString());
        }
        else
        {
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
            Assert.DoesNotContain(provisioner.Response.BootstrapToken, text);
            Assert.DoesNotContain("sensitive", text);
        }
        Assert.Equal(scenario is "valid" or "failure" ? 1 : 0, provisioner.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":1,\"lifetimeSeconds\":59}")]
    [InlineData("{\"schemaVersion\":1,\"lifetimeSeconds\":901}")]
    [InlineData("{\"schemaVersion\":2,\"lifetimeSeconds\":300}")]
    [InlineData("{\"schemaVersion\":1,\"lifetimeSeconds\":\"300\"}")]
    [InlineData("{\"schemaVersion\":1,\"lifetimeSeconds\":300,\"pendingEnrollmentId\":\"chosen\"}")]
    [InlineData("{\"schemaVersion\":1,\"lifetimeSeconds\":300,\"lifetimeSeconds\":300}")]
    [InlineData("{\"schemaVersion\":1,\"lifetimeSeconds\":300,\"\\u006cifetimeSeconds\":300}")]
    public async Task StrictBodyCannotReachProvisioner(string body)
    {
        var provisioner = new ProvisionerStub();
        var (app, token) = Start(provisioner);
        await using var host = app;
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var request = Request(body, token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, provisioner.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedBodyIsRejectedWithOrWithoutLength(bool unknownLength)
    {
        var provisioner = new ProvisionerStub();
        var (app, token) = Start(provisioner);
        await using var host = app;
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var request = Request(ValidBody, token);
        request.Content = new BodyContent(new byte[1025], unknownLength);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, provisioner.Calls);
    }

    [Fact]
    public async Task RateLimitCoversUnauthorizedRequestsAndCleartextIsRejected()
    {
        var provisioner = new ProvisionerStub();
        var (app, token) = Start(provisioner);
        await using var host = app;
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var cleartext = Request(ValidBody, token);
        cleartext.RequestUri = new Uri("http://localhost/api/v1/admin/pending-enrollments");
        using var denied = await client.SendAsync(cleartext);
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        for (var index = 0; index < 10; index++)
        {
            using var request = Request(ValidBody, null);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var last = Request(ValidBody, token);
        using var limited = await client.SendAsync(last);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Equal(0, provisioner.Calls);
    }

    private static (WebApplication App, string Token) Start(ProvisionerStub provisioner, bool enabled = true, bool permission = true)
    {
        using var key = RSA.Create(2048);
        var publicKey = new RsaSecurityKey(key.ExportParameters(false));
        var app = FleetApi.Build([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.Configure<JwtBearerOptions>(AdministratorAuthentication.Scheme, options =>
            {
                options.TokenValidationParameters.ValidIssuer = "https://identity.example.test";
                options.TokenValidationParameters.ValidAudience = "fleet-api";
                options.TokenValidationParameters.IssuerSigningKey = publicKey;
            });
            if (enabled)
                builder.Services.AddSingleton<IPendingEnrollmentProvisioner>(provisioner);
        });
        var claims = new List<Claim> { new("sub", "operator") };
        if (permission)
            claims.Add(new Claim("permission", "enrollments:create"));
        var token = new JwtSecurityToken("https://identity.example.test", "fleet-api", claims,
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256));
        token.Header["typ"] = "at+jwt";
        return (app, new JwtSecurityTokenHandler().WriteToken(token));
    }

    private static HttpRequestMessage Request(string body, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/api/v1/admin/pending-enrollments")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private sealed class ProvisionerStub : IPendingEnrollmentProvisioner
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public EnrollmentAdministrator? Administrator { get; private set; }
        public TimeSpan Lifetime { get; private set; }
        public PendingEnrollmentProvisioned Response { get; } = new(1, Guid.NewGuid(), new string('A', 43),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));

        public Task<PendingEnrollmentProvisioned> CreateAsync(EnrollmentAdministrator administrator, TimeSpan lifetime, CancellationToken cancellationToken)
        {
            Calls++;
            Administrator = administrator;
            Lifetime = lifetime;
            if (Fail)
                throw new IOException("sensitive provisioning failure");
            return Task.FromResult(Response);
        }
    }

    private sealed class BodyContent(byte[] bytes, bool unknownLength) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length)
        {
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
            length = bytes.Length;
            return !unknownLength;
        }
    }
}