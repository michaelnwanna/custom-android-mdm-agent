using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FleetManagement.Api;
using FleetManagement.Enrollment;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FleetManagement.Tests;

public sealed class EnrollmentHttpTests
{
    private static readonly string Token = new('A', 43);
    private static readonly string Body = JsonSerializer.Serialize(new
    {
        schemaVersion = 1, pendingEnrollmentId = Guid.NewGuid(), csrDer = Convert.ToBase64String(new byte[48])
    });

    [Theory]
    [InlineData(EnrollmentRedemptionStatus.Created, 201)]
    [InlineData(EnrollmentRedemptionStatus.Recovered, 200)]
    [InlineData(EnrollmentRedemptionStatus.Unauthorized, 401)]
    [InlineData(EnrollmentRedemptionStatus.Conflict, 409)]
    [InlineData(EnrollmentRedemptionStatus.Unavailable, 503)]
    public async Task DomainResultsMapToContractResponses(EnrollmentRedemptionStatus status, int expected)
    {
        var service = new RedemptionStub { Status = status };
        await using var app = await Start(service);
        using var client = app.GetTestClient();
        using var response = await client.SendAsync(Request());
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(1, service.Calls);
        if (expected >= 400)
            await AssertProblem(response);
        else
        {
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(service.Response.DeviceId, json.RootElement.GetProperty("deviceId").GetGuid());
        }
        if (expected == 401)
            Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        if (expected == 503)
            Assert.NotNull(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task ReplayUsesIdenticalResponseBytes()
    {
        var service = new RedemptionStub();
        await using var app = await Start(service);
        using var client = app.GetTestClient();
        using var first = await client.SendAsync(Request());
        service.Status = EnrollmentRedemptionStatus.Recovered;
        using var retry = await client.SendAsync(Request());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(await first.Content.ReadAsByteArrayAsync(), await retry.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Basic abc")]
    [InlineData("Bearer short")]
    [InlineData("Bearer AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    [InlineData("Bearer AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA, Bearer other")]
    public async Task InvalidAuthorizationIsRejectedBeforeService(string? authorization)
    {
        var service = new RedemptionStub();
        await using var app = await Start(service);
        using var client = app.GetTestClient();
        using var request = Request();
        request.Headers.Remove("Authorization");
        if (authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, service.Calls);
        await AssertProblem(response);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/json; charset=utf-16")]
    [InlineData("application/json; unexpected=true")]
    public async Task UnsupportedContentTypeIsRejected(string contentType)
    {
        var service = new RedemptionStub();
        await using var app = await Start(service);
        using var client = app.GetTestClient();
        using var request = Request();
        request.Content!.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BodyLimitAppliesWithOrWithoutContentLength(bool unknownLength)
    {
        var service = new RedemptionStub();
        await using var app = await Start(service);
        using var client = app.GetTestClient();
        using var request = Request();
        request.Content = new BodyContent(new byte[EnrollmentHttpRequest.MaximumBodyBytes + 1], unknownLength);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, service.Calls);
        await AssertProblem(response);
    }

    [Fact]
    public async Task MalformedBodyAndCleartextTransportCannotReachService()
    {
        var service = new RedemptionStub();
        await using var app = await Start(service);
        using var client = app.GetTestClient();
        using var invalid = Request();
        invalid.Content = new StringContent("{\"schemaVersion\":1,\"schemaVersion\":1}", Encoding.UTF8, "application/json");
        using var invalidResponse = await client.SendAsync(invalid);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        using var cleartext = Request();
        cleartext.RequestUri = new Uri("http://localhost/api/v1/enrollments");
        using var cleartextResponse = await client.SendAsync(cleartext);
        Assert.Equal(HttpStatusCode.BadRequest, cleartextResponse.StatusCode);
        Assert.Equal(0, service.Calls);
        Assert.Null(cleartextResponse.Headers.Location);
    }

    [Theory]
    [InlineData("csr", 400)]
    [InlineData("io", 503)]
    [InlineData("unexpected", 500)]
    public async Task ExceptionsCannotDiscloseCredentialsOrDetails(string failure, int status)
    {
        var service = new RedemptionStub { Failure = failure };
        await using var app = await Start(service);
        using var client = app.GetTestClient();
        using var response = await client.SendAsync(Request());
        Assert.Equal(status, (int)response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Token, text);
        Assert.DoesNotContain("sensitive", text);
        await AssertProblem(response);
    }

    [Fact]
    public async Task UnconfiguredHostFailsClosed()
    {
        await using var app = await Start(null);
        using var client = app.GetTestClient();
        using var response = await client.SendAsync(Request());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertProblem(response);
    }

    [Fact]
    public async Task RateLimitIncludesUnauthorizedRequestsBeforeService()
    {
        var service = new RedemptionStub();
        await using var app = await Start(service);
        using var client = app.GetTestClient();
        for (var index = 0; index < 60; index++)
        {
            using var request = Request();
            request.Headers.Remove("Authorization");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var limited = await client.SendAsync(Request());
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Equal(0, service.Calls);
        await AssertProblem(limited);
    }

    private static async Task<WebApplication> Start(RedemptionStub? service)
    {
        var app = FleetApi.Build([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            if (service is not null)
                builder.Services.AddSingleton<IEnrollmentRedemption>(service);
        });
        await app.StartAsync();
        return app;
    }

    private static HttpRequestMessage Request()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/api/v1/enrollments")
        {
            Content = new StringContent(Body, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return request;
    }

    private static async Task AssertProblem(HttpResponseMessage response)
    {
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("about:blank", json.RootElement.GetProperty("type").GetString());
        Assert.Equal((int)response.StatusCode, json.RootElement.GetProperty("status").GetInt32());
        Assert.All(json.RootElement.EnumerateObject(), property =>
            Assert.Contains(property.Name, new[] { "type", "title", "status", "traceId" }));
    }

    private sealed class RedemptionStub : IEnrollmentRedemption
    {
        public EnrollmentRedemptionStatus Status { get; set; } = EnrollmentRedemptionStatus.Created;
        public string? Failure { get; set; }
        public int Calls { get; private set; }
        public EnrollmentCertificateResponse Response { get; } = new(1, Guid.NewGuid(), "certificate", [],
            DateTimeOffset.Parse("2026-10-17T00:00:00Z"), DateTimeOffset.Parse("2026-10-18T00:00:00Z"));

        public Task<EnrollmentRedemption> RedeemAsync(EnrollmentHttpRequest request, string token, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(Token, token);
            if (Failure == "csr")
                throw new InvalidEnrollmentCsrException();
            if (Failure == "io")
                throw new IOException("sensitive " + token);
            if (Failure is not null)
                throw new InvalidOperationException("sensitive " + token);
            return Task.FromResult(new EnrollmentRedemption(Status, Response));
        }
    }

    private sealed class BodyContent(byte[] bytes, bool unknownLength) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = bytes.Length;
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return !unknownLength;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return stream.WriteAsync(bytes).AsTask();
        }
    }
}