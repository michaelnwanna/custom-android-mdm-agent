using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.RateLimiting;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FleetManagement.Api;

public static class FleetApi
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = EnrollmentHttpRequest.MaximumBodyBytes;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("enrollment", limiter =>
            {
                limiter.PermitLimit = 60;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
            options.OnRejected = async (context, cancellationToken) =>
            {
                var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? Math.Clamp((int)Math.Ceiling(retryAfter.TotalSeconds), 1, 86400) : 60;
                context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                await Problem(429).ExecuteAsync(context.HttpContext);
            };
        });
        ConfigureEnrollment(builder);
        configure?.Invoke(builder);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!context.Request.IsHttps)
            {
                await Problem(400).ExecuteAsync(context);
                return;
            }
            try
            {
                await next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                context.Abort();
            }
            catch (BadHttpRequestException error) when (error.StatusCode == 413)
            {
                await Problem(413).ExecuteAsync(context);
            }
            catch (Exception error) when (error is NpgsqlException or IOException or InvalidEnrollmentCertificateException)
            {
                context.Response.Headers.RetryAfter = "5";
                await Problem(503).ExecuteAsync(context);
            }
            catch (Exception)
            {
                await Problem(500).ExecuteAsync(context);
            }
        });
        app.UseRateLimiter();
        app.MapEnrollment();
        return app;
    }

    private static void ConfigureEnrollment(WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration.GetSection("Enrollment");
        var names = new[] { "ConnectionString", "DigestKeyFile", "CaCertificateFile", "CaKeyFile", "LedgerDirectory" };
        var values = names.Select(name => configuration[name]).ToArray();
        if (values.All(string.IsNullOrWhiteSpace))
            return;
        if (values.Any(string.IsNullOrWhiteSpace) || values.Skip(1).Any(value => !Path.IsPathFullyQualified(value!)))
            throw new InvalidOperationException("Enrollment requires complete explicit private lab configuration.");
        var digestKey = File.ReadAllBytes(values[1]!);
        if (digestKey.Length != 32)
            throw new InvalidOperationException("Enrollment digest key must contain exactly 32 random bytes.");
        var root = X509Certificate2.CreateFromPemFile(values[2]!, values[3]!);
        builder.Services.AddSingleton(root);
        builder.Services.AddPooledDbContextFactory<FleetDbContext>(options => options.UseNpgsql(values[0]));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<PostgresEnrollmentClaims>();
        builder.Services.AddSingleton<IEnrollmentSigner>(services => new LabLedgerEnrollmentSigner(values[4]!,
            services.GetRequiredService<X509Certificate2>(), services.GetRequiredService<TimeProvider>(), TimeSpan.FromDays(1)));
        builder.Services.AddSingleton(services => new EnrollmentSigningCoordinator(
            services.GetRequiredService<PostgresEnrollmentClaims>(), services.GetRequiredService<IEnrollmentSigner>(),
            services.GetRequiredService<X509Certificate2>(), TimeSpan.FromDays(2)));
        builder.Services.AddSingleton<IEnrollmentRedemption>(services => new EnrollmentRedemptionService(
            services.GetRequiredService<PostgresEnrollmentClaims>(), services.GetRequiredService<EnrollmentSigningCoordinator>(), digestKey));
    }

    private static void MapEnrollment(this WebApplication app)
    {
        app.MapPost("/api/v1/enrollments", async (HttpContext context) =>
        {
            var authorization = context.Request.Headers.Authorization;
            if (authorization.Count != 1 || !AuthenticationHeaderValue.TryParse(authorization[0], out var header) ||
                !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
                header.Parameter is not { Length: 43 } token ||
                token.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            {
                context.Response.Headers.WWWAuthenticate = "Bearer";
                return Problem(401);
            }
            if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var contentType) ||
                !string.Equals(contentType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
                contentType.Parameters.Any(parameter => !string.Equals(parameter.Name, "charset", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(parameter.Value?.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase)) ||
                context.Request.Headers.ContentEncoding.Count > 0)
                return Problem(415);
            if (context.Request.ContentLength > EnrollmentHttpRequest.MaximumBodyBytes)
                return Problem(413);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var buffer = new byte[EnrollmentHttpRequest.MaximumBodyBytes + 1];
                var read = 0;
                while (read < buffer.Length)
                {
                    var count = await context.Request.Body.ReadAsync(buffer.AsMemory(read), deadline.Token);
                    if (count == 0)
                        break;
                    read += count;
                }
                if (read > EnrollmentHttpRequest.MaximumBodyBytes)
                    return Problem(413);
                EnrollmentHttpRequest request;
                try
                {
                    request = EnrollmentHttpRequest.Parse(buffer.AsMemory(0, read));
                }
                catch (ArgumentException)
                {
                    return Problem(400);
                }
                var service = context.RequestServices.GetService<IEnrollmentRedemption>();
                if (service is null)
                {
                    context.Response.Headers.RetryAfter = "5";
                    return Problem(503);
                }
                EnrollmentRedemption result;
                try
                {
                    result = await service.RedeemAsync(request, token, deadline.Token);
                }
                catch (InvalidEnrollmentCsrException)
                {
                    return Problem(400);
                }
                switch (result.Status)
                {
                    case EnrollmentRedemptionStatus.Unauthorized:
                        context.Response.Headers.WWWAuthenticate = "Bearer";
                        return Problem(401);
                    case EnrollmentRedemptionStatus.Conflict:
                        return Problem(409);
                    case EnrollmentRedemptionStatus.Unavailable:
                        context.Response.Headers.RetryAfter = "5";
                        return Problem(503);
                    case EnrollmentRedemptionStatus.Created:
                    case EnrollmentRedemptionStatus.Recovered:
                        if (result.Response is null)
                            return Problem(500);
                        return Results.Json(result.Response, JsonSerializerOptions.Web,
                            statusCode: result.Status == EnrollmentRedemptionStatus.Created ? 201 : 200);
                    default:
                        return Problem(500);
                }
            }
            catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
            {
                context.Response.Headers.RetryAfter = "5";
                return Problem(503);
            }
        }).WithName("EnrollDevice").RequireRateLimiting("enrollment")
            .Accepts<EnrollmentHttpRequest>("application/json")
            .Produces<EnrollmentCertificateResponse>(201).Produces<EnrollmentCertificateResponse>(200)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(409).ProducesProblem(413)
            .ProducesProblem(415).ProducesProblem(429).ProducesProblem(500).ProducesProblem(503);
    }

    private static IResult Problem(int status)
        => Results.Problem(statusCode: status, type: "about:blank", title: status switch
        {
            400 => "Invalid request", 401 => "Unauthorized", 409 => "Enrollment conflict",
            413 => "Request body too large", 415 => "Unsupported media type", 429 => "Too many requests",
            503 => "Enrollment unavailable", _ => "Internal server error"
        });
}