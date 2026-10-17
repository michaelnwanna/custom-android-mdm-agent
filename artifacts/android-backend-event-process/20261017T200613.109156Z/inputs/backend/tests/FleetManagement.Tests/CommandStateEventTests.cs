using System.Text;
using System.Text.Json.Nodes;
using FleetManagement.Messaging;
using Xunit;

namespace FleetManagement.Tests;

public sealed class CommandStateEventTests
{
    private static readonly Guid DeviceId = Guid.Parse("a75df51f-ce19-40b7-8370-2c1a745bc823");
    private const string Message = """
        {"schemaVersion":1,"eventId":"ec648da6-0fde-4239-9305-2026e89d5911","commandId":"7512f600-bb5b-4353-b67a-3a56126308d7","deviceId":"a75df51f-ce19-40b7-8370-2c1a745bc823","correlationId":"374823b7-0e54-4893-b661-e638cc03310c","recordedAt":"2026-10-16T12:01:00Z","state":"received"}
        """;

    [Fact]
    public void ExpandsSharedContractAcceptanceAndRejectionFixtures()
    {
        var suite = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "command-events-v1.json")))!;
        var valid = suite["valid"]!.AsArray();
        Assert.Equal(5, valid.Count);
        Assert.Equal(new[] { CommandEventState.Received, CommandEventState.Failed, CommandEventState.Running,
            CommandEventState.Succeeded, CommandEventState.Expired }, valid.Select(node => Parse(node!.ToJsonString()).State));
        var invalid = suite["invalid"]!.AsArray();
        Assert.Equal(11, invalid.Count);
        foreach (var mutation in invalid)
        {
            var candidate = valid[mutation!["base"]!.GetValue<int>()]!.DeepClone().AsObject();
            if (mutation["set"] is JsonObject replacements)
                foreach (var field in replacements)
                    candidate[field.Key] = field.Value?.DeepClone();
            if (mutation["remove"] is JsonArray removed)
                foreach (var field in removed)
                    candidate.Remove(field!.GetValue<string>());
            Assert.Throws<ArgumentException>(() => Parse(candidate.ToJsonString()));
        }
    }

    [Fact]
    public void ValidatesExactVersionAndTimestampBounds()
    {
        foreach (var version in new[] { "1.0", "1e0", "10e-1", "0.1e1", "100.00e-2" })
            Assert.Equal(Parse(Message), Parse(Message.Replace("\"schemaVersion\":1", "\"schemaVersion\":" + version)));
        foreach (var version in new[] { "1.00000000000000000000000000001", "0.99999999999999999999999999999", "1e9999999999999", "1.1", "-1", "0" })
            Assert.Throws<ArgumentException>(() => Parse(Message.Replace("\"schemaVersion\":1", "\"schemaVersion\":" + version)));
        Assert.Equal("2026-10-16t12:01:00.123456789z", Parse(Message.Replace("2026-10-16T12:01:00Z", "2026-10-16t12:01:00.123456789z")).RecordedAt);
        Assert.Equal("2026-10-16T12:01:00-00:00", Parse(Message.Replace("12:01:00Z", "12:01:00-00:00")).RecordedAt);
        Assert.Equal("2026-10-16T12:01:00+23:59", Parse(Message.Replace("12:01:00Z", "12:01:00+23:59")).RecordedAt);
    }

    [Fact]
    public void ParsesBoundEventAndPreservesNanosecondTimestamp()
    {
        var parsed = Parse(Message.Replace("12:01:00Z", "14:01:00.123456789+02:00"));
        Assert.Equal(DeviceId, parsed.DeviceId);
        Assert.Equal(CommandEventState.Received, parsed.State);
        Assert.Equal("2026-10-16T14:01:00.123456789+02:00", parsed.RecordedAt);
        Assert.Null(parsed.ErrorCode);
        Assert.Equal("CommandStateEvent { [redacted] }", parsed.ToString());
        Assert.Equal(Parse(Message), Parse(Message.ToUpperInvariant().Replace("SCHEMAVERSION", "schemaVersion")
            .Replace("EVENTID", "eventId").Replace("COMMANDID", "commandId").Replace("DEVICEID", "deviceId")
            .Replace("CORRELATIONID", "correlationId").Replace("RECORDEDAT", "recordedAt")
            .Replace("STATE", "state").Replace("RECEIVED", "received")));
    }

    [Fact]
    public void RejectsMalformedWireAndBinding()
    {
        string[] invalid = ["", "[]", "null", Message + "{}", Message + "//comment",
            Message.Replace("\"state\":\"received\"", "\"state\":\"received\",\"state\":\"failed\""),
            Message.Replace("\"schemaVersion\":1", "\"schemaVersion\":true"),
            Message.Replace("\"schemaVersion\":1", "\"schemaVersion\":\"1\""),
            Message.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"),
            Message.Replace("12:01:00Z", "12:01:00"), Message.Replace("12:01:00Z", "24:01:00Z"),
            Message.Replace("12:01:00Z", "12:01:60Z"), Message.Replace("12:01:00Z", "12:01:00.1234567890Z"),
            Message.Replace("2026-10-16", "2026-02-30"), Message.Replace("12:01:00Z", "12:01:00+24:00"),
            Message.Replace("ec648da6-0fde-4239-9305-2026e89d5911", "{ec648da6-0fde-4239-9305-2026e89d5911}"),
            Message.Replace("ec648da6-0fde-4239-9305-2026e89d5911", " ec648da6-0fde-4239-9305-2026e89d5911 "),
            Message.Replace("\"state\":\"received\"", "\"state\":null")];
        foreach (var candidate in invalid)
            Assert.Throws<ArgumentException>(() => Parse(candidate));
        Assert.Throws<ArgumentException>(() => CommandStateEvent.Parse(Encoding.UTF8.GetBytes(Message), Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => CommandStateEvent.Parse(new byte[] { 0xc3, 0x28 }, DeviceId));
        Assert.Throws<ArgumentException>(() => Parse(Message.Replace("received", "bad\ufffd")));
        Assert.Throws<ArgumentException>(() => Parse(Message + new string(' ', CommandStateEvent.MaximumBodyBytes)));
        Assert.Equal(Parse(Message), Parse(Message.PadRight(CommandStateEvent.MaximumBodyBytes)));
    }

    [Fact]
    public void RequiresEveryFieldAndStrictFailureCode()
    {
        var root = JsonNode.Parse(Message)!.AsObject();
        foreach (var field in root.Select(property => property.Key).ToArray())
        {
            var reduced = root.DeepClone().AsObject();
            reduced.Remove(field);
            Assert.Throws<ArgumentException>(() => Parse(reduced.ToJsonString()));
        }
        var failed = Message.Replace("\"state\":\"received\"", "\"state\":\"failed\",\"error\":{\"code\":\"FAILED\"}");
        Assert.Equal("FAILED", Parse(failed).ErrorCode);
        foreach (var code in new[] { "", "bad", "A-B", "A\n", new string('A', 65) })
        {
            var changed = JsonNode.Parse(failed)!;
            changed["error"]!["code"] = code;
            Assert.Throws<ArgumentException>(() => Parse(changed.ToJsonString()));
        }
        Assert.Throws<ArgumentException>(() => Parse(failed.Replace("\"code\":\"FAILED\"", "\"code\":\"FAILED\",\"code\":\"OTHER\"")));
        Assert.Throws<ArgumentException>(() => Parse(failed.Replace("\"code\":\"FAILED\"", "\"code\":\"FAILED\",\"detail\":\"secret\"")));
        Assert.Equal(new string('A', 64), Parse(failed.Replace("FAILED", new string('A', 64))).ErrorCode);
    }

    private static CommandStateEvent Parse(string text) => CommandStateEvent.Parse(Encoding.UTF8.GetBytes(text), DeviceId);
}