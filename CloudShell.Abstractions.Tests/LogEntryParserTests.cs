using CloudShell.Abstractions.Logs;

namespace CloudShell.Abstractions.Tests;

public sealed class LogEntryParserTests
{
    [Fact]
    public void ParseLine_MapsJsonConsoleFields()
    {
        var fallbackTimestamp = DateTimeOffset.Parse("2026-08-08T09:00:00Z");

        var entry = LogEntryParser.ParseLine(
            """
            {"Timestamp":"2026-08-08T10:00:00Z","LogLevel":"Warning","Category":"Sample.Worker","EventId":42,"Message":"Processed item","State":{"itemId":"item-1"}}
            """,
            "fallback",
            null,
            LogFormat.JsonConsole,
            fallbackTimestamp);

        Assert.Equal(DateTimeOffset.Parse("2026-08-08T10:00:00Z"), entry.Timestamp);
        Assert.Equal("Processed item", entry.Message);
        Assert.Equal("Warning", entry.Severity);
        Assert.Equal("Sample.Worker", entry.Category);
        Assert.Equal("42", entry.EventId);
        Assert.Equal("item-1", entry.Attributes?["itemId"]);
    }

    [Fact]
    public void ParseLine_PreservesMalformedJsonAsPlainText()
    {
        var timestamp = DateTimeOffset.Parse("2026-08-08T10:00:00Z");

        var entry = LogEntryParser.ParseLine(
            "{not-json",
            "application",
            "Error",
            LogFormat.JsonConsole,
            timestamp);

        Assert.Equal("{not-json", entry.Message);
        Assert.Equal("application", entry.Source);
        Assert.Equal("Error", entry.Severity);
        Assert.Equal(timestamp, entry.Timestamp);
    }
}
