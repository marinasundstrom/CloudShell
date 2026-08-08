using CloudShell.Abstractions.Logs;
using System.Globalization;

namespace CloudShell.ControlPlane.Providers;

internal static class ContainerApplicationRuntimeLogParser
{
    public static LogEntry ParseProcessOutputLine(
        string line,
        string source,
        string? severity,
        LogFormat format,
        DateTimeOffset timestamp) =>
        LogEntryParser.ParseLine(line, source, severity, format, timestamp);

    public static LogEntry ParseContainerLogLine(
        string line,
        string source,
        string? severity,
        LogFormat format)
    {
        var normalized = line.TrimEnd('\r');
        var timestamp = DateTimeOffset.UtcNow;
        var message = normalized;
        var separatorIndex = normalized.IndexOf(' ');
        if (separatorIndex > 0 &&
            DateTimeOffset.TryParse(
                normalized[..separatorIndex],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsedTimestamp))
        {
            timestamp = parsedTimestamp;
            message = normalized[(separatorIndex + 1)..];
        }

        return ParseProcessOutputLine(message, source, severity, format, timestamp);
    }
}
