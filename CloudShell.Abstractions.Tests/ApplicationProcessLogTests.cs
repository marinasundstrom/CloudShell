using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class ApplicationProcessLogTests
{
    [Fact]
    public void Append_ParsesJsonConsoleLogMetadata()
    {
        var log = new ApplicationProcessLog();
        var line = """
            {"Timestamp":"2026-06-13T22:38:38.4626500+00:00","EventId":42,"LogLevel":"Information","Category":"CloudShell.ProjectReference.Frontend","Message":"Calling referenced API at http://localhost:21067/\n","State":{"ResolvedEndpoint":"http://localhost:21067/","{OriginalFormat}":"Calling referenced API at {ResolvedEndpoint}"},"Scopes":[{"Message":"SpanId: 0123456789abcdef, TraceId: 0123456789abcdef0123456789abcdef, ParentId: 1111111111111111","SpanId":"0123456789abcdef","TraceId":"0123456789abcdef0123456789abcdef","ParentId":"1111111111111111"}]}
            """;

        log.Append(line, "stdout");

        var entry = Assert.Single(log.Read(10, before: null));
        Assert.Equal("Calling referenced API at http://localhost:21067/", entry.Message);
        Assert.Equal("Information", entry.Severity);
        Assert.Equal("stdout", entry.Source);
        Assert.Equal("42", entry.EventId);
        Assert.Equal("CloudShell.ProjectReference.Frontend", entry.Category);
        Assert.Equal("0123456789abcdef0123456789abcdef", entry.TraceId);
        Assert.Equal("0123456789abcdef", entry.SpanId);
        Assert.Equal("http://localhost:21067/", entry.Attributes?["ResolvedEndpoint"]);
        Assert.Equal("Calling referenced API at {ResolvedEndpoint}", entry.Attributes?["log.originalFormat"]);
    }

    [Fact]
    public void Read_PreservesStructuredMetadataFromLogFile()
    {
        var logPath = Path.Combine(
            Path.GetTempPath(),
            "cloudshell-tests",
            $"{Guid.NewGuid():N}.log");
        var line = """
            {"timestamp":"2026-06-13T22:38:38.4626500+00:00","severity":"Warning","source":"stdout","eventId":"sample.warning","category":"CloudShell.ProjectReference.Api","message":"API returned a warning","traceId":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","spanId":"bbbbbbbbbbbbbbbb","attributes":{"machine":"test-host"}}
            """;

        try
        {
            var writer = new ApplicationProcessLog(logPath);
            writer.Append(line, "stdout");

            var reader = new ApplicationProcessLog(logPath);
            var entry = Assert.Single(reader.Read(10, before: null));

            Assert.Equal("API returned a warning", entry.Message);
            Assert.Equal("Warning", entry.Severity);
            Assert.Equal("sample.warning", entry.EventId);
            Assert.Equal("CloudShell.ProjectReference.Api", entry.Category);
            Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", entry.TraceId);
            Assert.Equal("bbbbbbbbbbbbbbbb", entry.SpanId);
            Assert.Equal("test-host", entry.Attributes?["machine"]);
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [Fact]
    public void Append_KeepsMalformedJsonLikeOutputAsPlainText()
    {
        var log = new ApplicationProcessLog();

        log.Append("{not-json", "stdout");

        var entry = Assert.Single(log.Read(10, before: null));
        Assert.Equal("{not-json", entry.Message);
        Assert.Equal("stdout", entry.Source);
        Assert.Null(entry.Attributes);
    }
}
