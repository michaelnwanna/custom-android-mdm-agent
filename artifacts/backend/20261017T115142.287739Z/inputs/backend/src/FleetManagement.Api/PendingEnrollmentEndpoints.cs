using System.Net.Http.Headers;
using System.Text.Json;
using FleetManagement.Enrollment;

namespace FleetManagement.Api;

public static class PendingEnrollmentEndpoints
{
    public static void MapPendingEnrollment(this WebApplication app)
    {
        app.MapPost("/api/v1/admin/pending-enrollments", async (HttpContext context) =>
        {
            if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var contentType) ||
                !string.Equals(contentType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
                contentType.Parameters.Any(parameter => !string.Equals(parameter.Name, "charset", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(parameter.Value?.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase)) ||
                context.Request.Headers.ContentEncoding.Count > 0)
                return FleetApi.Problem(415);
            if (context.Request.ContentLength > 1024)
                return FleetApi.Problem(413);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var buffer = new byte[1025];
                var read = 0;
                while (read < buffer.Length)
                {
                    var count = await context.Request.Body.ReadAsync(buffer.AsMemory(read), deadline.Token);
                    if (count == 0)
                        break;
                    read += count;
                }
                if (read > 1024)
                    return FleetApi.Problem(413);
                int seconds;
                try
                {
                    using var document = JsonDocument.Parse(buffer.AsMemory(0, read), new JsonDocumentOptions { MaxDepth = 2 });
                    var root = document.RootElement;
                    var properties = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in root.EnumerateObject())
                        if (!properties.Add(property.Name) || property.Name is not ("schemaVersion" or "lifetimeSeconds"))
                            return FleetApi.Problem(400);
                    if (properties.Count != 2 || !root.GetProperty("schemaVersion").TryGetInt32(out var version) || version != 1 ||
                        !root.GetProperty("lifetimeSeconds").TryGetInt32(out seconds) || seconds is < 60 or > 900)
                        return FleetApi.Problem(400);
                }
                catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
                {
                    return FleetApi.Problem(400);
                }
                var provisioner = context.RequestServices.GetService<IPendingEnrollmentProvisioner>();
                if (provisioner is null)
                {
                    context.Response.Headers.RetryAfter = "5";
                    return FleetApi.Problem(503);
                }
                var administrator = new EnrollmentAdministrator(context.User.FindFirst("iss")!.Value,
                    context.User.FindFirst("sub")!.Value);
                var response = await provisioner.CreateAsync(administrator, TimeSpan.FromSeconds(seconds), deadline.Token);
                return Results.Json(response, JsonSerializerOptions.Web, statusCode: 201);
            }
            catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
            {
                context.Response.Headers.RetryAfter = "5";
                return FleetApi.Problem(503);
            }
        }).WithName("CreatePendingEnrollment").RequireAuthorization(AdministratorAuthentication.ProvisionPolicy)
            .RequireRateLimiting("provision-enrollment")
            .Produces<PendingEnrollmentProvisioned>(201).ProducesProblem(400).ProducesProblem(401)
            .ProducesProblem(403).ProducesProblem(413).ProducesProblem(415).ProducesProblem(429)
            .ProducesProblem(500).ProducesProblem(503);
    }
}