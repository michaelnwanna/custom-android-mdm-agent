using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using FleetManagement.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;

namespace FleetManagement.Tests;

public sealed class AdministratorAuthenticationTests
{
    [Fact]
    public async Task ConfiguredAudienceAndDiscoveryIssuerAreEnforcedByBearerHandler()
    {
        using var key = RSA.Create(2048);
        var metadata = new OpenIdConnectConfiguration { Issuer = "https://identity.example.test" };
        metadata.SigningKeys.Add(new RsaSecurityKey(key));
        await using var app = FleetApi.Build([
            "--Administrator:Authority", "https://identity.example.test",
            "--Administrator:Audience", "fleet-api"
        ], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.Configure<JwtBearerOptions>(AdministratorAuthentication.Scheme,
                options => options.Configuration = metadata);
        });
        app.MapGet("/admin-test", () => Results.Ok()).RequireAuthorization(AdministratorAuthentication.ProvisionPolicy);
        await app.StartAsync();
        using var client = app.GetTestClient();
        foreach (var scenario in new[] { "valid", "issuer", "audience" })
        {
            var token = new JwtSecurityToken(scenario == "issuer" ? "https://other.test" : metadata.Issuer,
                scenario == "audience" ? "other-api" : "fleet-api",
                [new Claim("sub", "operator"), new Claim("permission", "enrollments:create")],
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5),
                new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256));
            token.Header["typ"] = "at+jwt";
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/admin-test");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            using var response = await client.SendAsync(request);
            Assert.Equal(scenario == "valid" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task UnconfiguredAuthorityRejectsEvenWellFormedSignedAccessToken()
    {
        using var key = RSA.Create(2048);
        await using var app = FleetApi.Build([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
        });
        await app.StartAsync();
        var token = new JwtSecurityToken("https://untrusted.example.test", "fleet-api",
            [new Claim("sub", "operator"), new Claim("permission", "enrollments:create")],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256));
        token.Header["typ"] = "at+jwt";
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/api/v1/admin/pending-enrollments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        using var client = app.GetTestClient();
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("http://identity.example.test", "fleet-api")]
    [InlineData("https://identity.example.test", null)]
    [InlineData(null, "fleet-api")]
    [InlineData("https://identity.example.test?query=true", "fleet-api")]
    public void PartialOrUnsafeAuthorityConfigurationFails(string? authority, string? audience)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Administrator:Authority"] = authority,
            ["Administrator:Audience"] = audience
        });
        Assert.Throws<InvalidOperationException>(() => builder.AddAdministratorAuthentication());
    }

    [Theory]
    [InlineData("valid", 200)]
    [InlineData("multiple-scopes", 200)]
    [InlineData("scope", 403)]
    [InlineData("role", 403)]
    [InlineData("generic-permission-only", 403)]
    [InlineData("at+jwt", 401)]
    [InlineData("id-token-audience", 401)]
    public async Task EntraProfileRequiresDelegatedScopeAndAssignedAppRole(string scenario, int expected)
    {
        const string issuer = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0";
        const string apiClientId = "22222222-2222-2222-2222-222222222222";
        using var key = RSA.Create(2048);
        var metadata = new OpenIdConnectConfiguration { Issuer = issuer };
        metadata.SigningKeys.Add(new RsaSecurityKey(key));
        await using var app = FleetApi.Build([
            "--Administrator:Authority", issuer,
            "--Administrator:Audience", apiClientId,
            "--Administrator:TokenProfile", "Entra"
        ], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.Configure<JwtBearerOptions>(AdministratorAuthentication.Scheme,
                options => options.Configuration = metadata);
        });
        app.MapGet("/admin-test", () => Results.Ok()).RequireAuthorization(AdministratorAuthentication.ProvisionPolicy);
        await app.StartAsync();
        var claims = new List<Claim> { new("sub", "pairwise-subject") };
        if (scenario == "multiple-scopes")
            claims.Add(new Claim("scp", "Devices.Read Enrollments.Create"));
        else if (scenario is not ("scope" or "generic-permission-only"))
            claims.Add(new Claim("scp", "Enrollments.Create"));
        if (scenario is not ("role" or "generic-permission-only"))
            claims.Add(new Claim("roles", "Enrollments.Create"));
        if (scenario == "generic-permission-only")
            claims.Add(new Claim("permission", "enrollments:create"));
        var token = new JwtSecurityToken(issuer,
            scenario == "id-token-audience" ? "33333333-3333-3333-3333-333333333333" : apiClientId, claims,
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256));
        token.Header["typ"] = scenario == "at+jwt" ? "at+jwt" : "JWT";
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/admin-test");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        using var client = app.GetTestClient();
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, (int)response.StatusCode);
    }

    [Fact]
    public void UnknownTokenProfileFails()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Administrator:Authority"] = "https://identity.example.test",
            ["Administrator:Audience"] = "fleet-api",
            ["Administrator:TokenProfile"] = "entra"
        });
        Assert.Throws<InvalidOperationException>(() => builder.AddAdministratorAuthentication());
    }

    [Theory]
    [InlineData("valid", 200)]
    [InlineData("missing", 401)]
    [InlineData("permission", 403)]
    [InlineData("subject", 403)]
    [InlineData("issuer", 401)]
    [InlineData("audience", 401)]
    [InlineData("expired", 401)]
    [InlineData("signature", 401)]
    [InlineData("id-token", 401)]
    public async Task SignedAccessTokensRequireExactAdministratorBoundary(string scenario, int expected)
    {
        using var key = RSA.Create(2048);
        using var otherKey = RSA.Create(2048);
        await using var app = FleetApi.Build([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.Configure<JwtBearerOptions>(AdministratorAuthentication.Scheme, options =>
            {
                options.TokenValidationParameters.ValidIssuer = "https://identity.example.test";
                options.TokenValidationParameters.ValidAudience = "fleet-api";
                options.TokenValidationParameters.IssuerSigningKey = new RsaSecurityKey(key);
            });
        });
        app.MapGet("/admin-test", () => Results.Ok()).RequireAuthorization(AdministratorAuthentication.ProvisionPolicy);
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/admin-test");
        if (scenario != "missing")
        {
            var claims = new List<Claim>();
            if (scenario != "subject")
                claims.Add(new Claim("sub", "operator"));
            if (scenario != "permission")
                claims.Add(new Claim("permission", "enrollments:create"));
            var now = DateTime.UtcNow;
            var token = new JwtSecurityToken(scenario == "issuer" ? "https://other.test" : "https://identity.example.test",
                scenario == "audience" ? "other-api" : "fleet-api", claims,
                now.AddMinutes(-10), scenario == "expired" ? now.AddMinutes(-1) : now.AddMinutes(5),
                new SigningCredentials(new RsaSecurityKey(scenario == "signature" ? otherKey : key), SecurityAlgorithms.RsaSha256));
            token.Header["typ"] = scenario == "id-token" ? "JWT" : "at+jwt";
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        if (expected >= 400)
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }
}