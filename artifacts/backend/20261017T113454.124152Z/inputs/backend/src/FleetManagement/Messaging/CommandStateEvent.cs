using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FleetManagement.Messaging;

public enum CommandEventState { Received, Running, Succeeded, Failed, Expired }

public sealed record CommandStateEvent(Guid EventId, Guid CommandId, Guid DeviceId, Guid CorrelationId,
    string RecordedAt, CommandEventState State, string? ErrorCode)
{
    public const int MaximumBodyBytes = 8192;
    private static readonly Regex Timestamp = new(
        @"\A(?<date>[0-9]{4}-[0-9]{2}-[0-9]{2})[Tt](?<hour>[0-9]{2}):(?<minute>[0-9]{2}):(?<second>[0-9]{2})(?:\.[0-9]{1,9})?(?:[Zz]|[+-](?<offsetHour>[0-9]{2}):(?<offsetMinute>[0-9]{2}))\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Error = new(@"\A[A-Z][A-Z0-9_]{0,63}\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static CommandStateEvent Parse(ReadOnlyMemory<byte> body, Guid authenticatedDeviceId)
    {
        if (body.Length is 0 or > MaximumBodyBytes)
            throw Invalid();
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 3 });
            var root = document.RootElement;
            Fields(root, ["schemaVersion", "eventId", "commandId", "deviceId", "correlationId", "recordedAt", "state"], "error");
            if (!VersionOne(root.GetProperty("schemaVersion")))
                throw Invalid();
            var deviceId = Identifier(root, "deviceId");
            if (deviceId != authenticatedDeviceId)
                throw Invalid();
            var state = Text(root, "state") switch
            {
                "received" => CommandEventState.Received,
                "running" => CommandEventState.Running,
                "succeeded" => CommandEventState.Succeeded,
                "failed" => CommandEventState.Failed,
                "expired" => CommandEventState.Expired,
                _ => throw Invalid(),
            };
            string? code = null;
            var hasError = root.TryGetProperty("error", out var error);
            if (hasError != (state == CommandEventState.Failed))
                throw Invalid();
            if (hasError)
            {
                Fields(error, ["code"]);
                code = Text(error, "code");
                if (!Error.IsMatch(code))
                    throw Invalid();
            }
            var recordedAt = Text(root, "recordedAt");
            var match = Timestamp.Match(recordedAt);
            if (!match.Success || !DateOnly.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture) > 23 ||
                int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture) > 59 ||
                int.Parse(match.Groups["second"].Value, CultureInfo.InvariantCulture) > 59 ||
                (match.Groups["offsetHour"].Success &&
                    (int.Parse(match.Groups["offsetHour"].Value, CultureInfo.InvariantCulture) > 23 ||
                     int.Parse(match.Groups["offsetMinute"].Value, CultureInfo.InvariantCulture) > 59)))
                throw Invalid();
            return new CommandStateEvent(Identifier(root, "eventId"), Identifier(root, "commandId"), deviceId,
                Identifier(root, "correlationId"), recordedAt, state, code);
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw Invalid();
        }
    }

    private static void Fields(JsonElement element, string[] required, string? optional = null)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw Invalid();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!names.Add(property.Name) || (!required.Contains(property.Name) && property.Name != optional))
                throw Invalid();
        if (required.Any(name => !names.Contains(name)))
            throw Invalid();
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw Invalid();

    private static bool VersionOne(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number)
            return false;
        var number = element.GetRawText();
        var exponentStart = number.IndexOfAny(['e', 'E']);
        var exponent = 0;
        if (exponentStart >= 0 && !int.TryParse(number.AsSpan(exponentStart + 1), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out exponent))
            return false;
        var mantissa = exponentStart >= 0 ? number[..exponentStart] : number;
        var point = mantissa.IndexOf('.');
        var fractionLength = point >= 0 ? mantissa.Length - point - 1 : 0;
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        var significant = digits.TrimEnd('0');
        return significant == "1" && (long)exponent - fractionLength + digits.Length - significant.Length == 0;
    }

    private static Guid Identifier(JsonElement element, string name) =>
        Guid.TryParseExact(Text(element, name), "D", out var identifier) ? identifier : throw Invalid();

    private static ArgumentException Invalid() => new("Invalid command state event.");

    public override string ToString() => "CommandStateEvent { [redacted] }";
}