using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FleetManagement.Api;
using FleetManagement.Enrollment;
using FleetManagement.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FleetManagement.Tests;

public sealed class CommandEventHttpTests
{
    [Fact]
    public async Task CertificateIdentityIsRequiredAndAcknowledgementWaitsForIngestion()
    {
        var deviceId = Guid.NewGuid();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var certificate = new CertificateRequest("CN=Pipeline identity stub", key, HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var service = new IngestionStub();
        await using var app = FleetApi.Build([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<IDeviceCertificateIdentity>(new IdentityStub(deviceId));
            builder.Services.AddSingleton<ICommandEventIngestion>(service);
        });
        await app.StartAsync();
        var server = app.GetTestServer();
        var bytes = Encoding.UTF8.GetBytes($$"""{"schemaVersion":1,"eventId":"{{Guid.NewGuid()}}","commandId":"{{Guid.NewGuid()}}","deviceId":"{{deviceId}}","correlationId":"{{Guid.NewGuid()}}","state":"received","recordedAt":"2026-10-17T12:00:00Z"}""");
        var denied = await server.SendAsync(context => Prepare(context, bytes, null));
        Assert.Equal(401, denied.Response.StatusCode);
        Assert.Equal(0, service.Calls);
        var pending = server.SendAsync(context => Prepare(context, bytes, certificate));
        await service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(pending.IsCompleted);
        Assert.Equal(deviceId, service.DeviceId);
        Assert.Equal(bytes, service.Body);
        var acknowledgement = $$"""{"schemaVersion":1,"deviceId":"{{deviceId}}","eventId":"{{Guid.NewGuid()}}","receivedAt":"2026-10-17T12:00:01Z"}""";
        service.Completion.SetResult(new DurableCommandEventReceipt(acknowledgement, false));
        var accepted = await pending;
        Assert.Equal(200, accepted.Response.StatusCode);
        Assert.Equal("no-store", accepted.Response.Headers.CacheControl.ToString());
        using var reader = new StreamReader(accepted.Response.Body);
        Assert.Equal(acknowledgement, await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task RequestBoundsErrorsAndDuplicateRepliesPreserveTheBoundary()
    {
        var deviceId = Guid.NewGuid();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var certificate = new CertificateRequest("CN=Pipeline identity stub", key, HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var service = new IngestionStub();
        await using var app = FleetApi.Build([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<IDeviceCertificateIdentity>(new IdentityStub(deviceId));
            builder.Services.AddSingleton<ICommandEventIngestion>(service);
        });
        await app.StartAsync();
        var server = app.GetTestServer();
        var bytes = Encoding.UTF8.GetBytes($$"""{"schemaVersion":1,"eventId":"{{Guid.NewGuid()}}","commandId":"{{Guid.NewGuid()}}","deviceId":"{{deviceId}}","correlationId":"{{Guid.NewGuid()}}","state":"received","recordedAt":"2026-10-17T12:00:00Z"}""");
        foreach (var contentType in new[] { "text/plain", "application/json; charset=utf-16", "application/json; extra=1" })
        {
            var response = await server.SendAsync(context =>
            {
                Prepare(context, bytes, certificate);
                context.Request.ContentType = contentType;
            });
            Assert.Equal(415, response.Response.StatusCode);
        }
        var encoded = await server.SendAsync(context =>
        {
            Prepare(context, bytes, certificate);
            context.Request.Headers.ContentEncoding = "gzip";
        });
        Assert.Equal(415, encoded.Response.StatusCode);
        var insecure = await server.SendAsync(context =>
        {
            Prepare(context, bytes, certificate);
            context.Request.Scheme = "http";
        });
        Assert.Equal(400, insecure.Response.StatusCode);
        var oversized = await server.SendAsync(context => Prepare(context, new byte[8193], certificate));
        Assert.Equal(413, oversized.Response.StatusCode);
        var declared = await server.SendAsync(context =>
        {
            Prepare(context, bytes, certificate);
            context.Request.ContentLength = 8193;
        });
        Assert.Equal(413, declared.Response.StatusCode);
        foreach (var body in new[] { Array.Empty<byte>(), Encoding.UTF8.GetBytes("{}"),
            Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace(deviceId.ToString(), Guid.NewGuid().ToString())),
            Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"state\":", "\"schemaVersion\":1,\"state\":")) })
        {
            var response = await server.SendAsync(context => Prepare(context, body, certificate));
            Assert.Equal(400, response.Response.StatusCode);
        }
        Assert.Equal(0, service.Calls);
        var acknowledgement = $$"""{"schemaVersion":1,"deviceId":"{{deviceId}}","eventId":"{{Guid.NewGuid()}}","receivedAt":"2026-10-17T12:00:01Z"}""";
        service.Completion.SetResult(new DurableCommandEventReceipt(acknowledgement, true));
        var duplicate = await server.SendAsync(context => Prepare(context, bytes, certificate));
        Assert.Equal(200, duplicate.Response.StatusCode);
        Assert.StartsWith("application/json", duplicate.Response.ContentType);
        using (var reader = new StreamReader(duplicate.Response.Body))
            Assert.Equal(acknowledgement, await reader.ReadToEndAsync());
        foreach (var failure in new Exception[] { new CommandEventRejectedException(), new InvalidOperationException("private detail"),
            new IOException("private detail"), new OperationCanceledException("private detail") })
        {
            service.Failure = failure;
            var response = await server.SendAsync(context => Prepare(context, bytes, certificate));
            Assert.Equal(failure is CommandEventRejectedException ? 409 : failure is InvalidOperationException ? 500 : 503,
                response.Response.StatusCode);
            using var reader = new StreamReader(response.Response.Body);
            Assert.DoesNotContain("private detail", await reader.ReadToEndAsync());
            Assert.Equal("no-store", response.Response.Headers.CacheControl.ToString());
        }
    }

    private static void Prepare(HttpContext context, byte[] body, X509Certificate2? certificate)
    {
        context.Request.Scheme = "https";
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/device/command-events";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(body);
        context.Connection.ClientCertificate = certificate;
        context.Request.Headers.Authorization = "Bearer ignored";
        context.Request.Headers["X-ARR-ClientCert"] = "ignored";
    }

    private sealed class IdentityStub(Guid deviceId) : IDeviceCertificateIdentity
    {
        public Task<Guid?> AuthenticateAsync(X509Certificate2 certificate, CancellationToken cancellationToken)
            => Task.FromResult<Guid?>(deviceId);
    }

    private sealed class IngestionStub : ICommandEventIngestion
    {
        public int Calls { get; private set; }
        public Guid DeviceId { get; private set; }
        public byte[]? Body { get; private set; }
        public Exception? Failure { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<DurableCommandEventReceipt> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<DurableCommandEventReceipt> IngestAsync(ReadOnlyMemory<byte> body, Guid authenticatedDeviceId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            DeviceId = authenticatedDeviceId;
            Body = body.ToArray();
            if (Failure is not null)
                throw Failure;
            Entered.TrySetResult();
            return Completion.Task.WaitAsync(cancellationToken);
        }
    }
}