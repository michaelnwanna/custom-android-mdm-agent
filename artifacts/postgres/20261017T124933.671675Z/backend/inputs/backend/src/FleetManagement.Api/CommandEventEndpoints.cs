using System.Net.Http.Headers;
using FleetManagement.Messaging;

namespace FleetManagement.Api;

public static class CommandEventEndpoints
{
    public static void MapCommandEvents(this WebApplication app)
    {
        app.MapPost("/api/v1/device/command-events", async (HttpContext context) =>
        {
            if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var contentType) ||
                !string.Equals(contentType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
                contentType.Parameters.Any(parameter => !string.Equals(parameter.Name, "charset", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(parameter.Value?.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase)) ||
                context.Request.Headers.ContentEncoding.Count > 0)
                return FleetApi.Problem(415);
            if (context.Request.ContentLength > CommandStateEvent.MaximumBodyBytes)
                return FleetApi.Problem(413);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var buffer = new byte[CommandStateEvent.MaximumBodyBytes + 1];
                var read = 0;
                while (read < buffer.Length)
                {
                    var count = await context.Request.Body.ReadAsync(buffer.AsMemory(read), deadline.Token);
                    if (count == 0)
                        break;
                    read += count;
                }
                if (read > CommandStateEvent.MaximumBodyBytes)
                    return FleetApi.Problem(413);
                var deviceId = Guid.Parse(context.User.FindFirst("device_id")!.Value);
                CommandStateEvent.Parse(buffer.AsMemory(0, read), deviceId);
                var service = context.RequestServices.GetService<ICommandEventIngestion>();
                if (service is null)
                {
                    context.Response.Headers.RetryAfter = "5";
                    return FleetApi.Problem(503);
                }
                var receipt = await service.IngestAsync(buffer.AsMemory(0, read), deviceId, deadline.Token);
                return Results.Text(receipt.AcknowledgementJson, "application/json", statusCode: 200);
            }
            catch (ArgumentException)
            {
                return FleetApi.Problem(400);
            }
            catch (CommandEventRejectedException)
            {
                return Results.Problem(statusCode: 409, type: "about:blank", title: "Event conflict");
            }
            catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
            {
                context.Response.Headers.RetryAfter = "5";
                return FleetApi.Problem(503);
            }
        }).WithName("IngestCommandEvent").RequireAuthorization(DeviceAuthentication.Policy).RequireRateLimiting("device")
            .Accepts<CommandStateEvent>("application/json")
            .Produces(200, contentType: "application/json")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409)
            .ProducesProblem(413).ProducesProblem(415).ProducesProblem(429).ProducesProblem(500).ProducesProblem(503);
    }
}