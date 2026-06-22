using CloudShell.Abstractions.Logs;
using System.Globalization;
using System.Text.Json;

namespace CloudShell.Providers.Applications;

internal static class ApplicationRuntimeLogParser
{
    public static IReadOnlyList<LogEntry> ParseContainerLogOutput(
        string output,
        string source,
        string? severity,
        LogFormat format)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => ParseContainerLogLine(line, source, severity, format))
            .ToArray();
    }

    private static LogEntry ParseContainerLogLine(
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

        if (ShouldParseStructuredJson(format) &&
            TryParseStructuredJsonLog(message, source, severity, timestamp) is { } structured)
        {
            return structured;
        }

        return new LogEntry(timestamp, message, severity, source);
    }

    private static bool ShouldParseStructuredJson(LogFormat format) =>
        format is LogFormat.JsonConsole or
            LogFormat.SerilogCompactJson or
            LogFormat.Structured;

    private static LogEntry? TryParseStructuredJsonLog(
        string line,
        string? fallbackSource,
        string? fallbackSeverity,
        DateTimeOffset fallbackTimestamp)
    {
        if (!line.TrimStart().StartsWith('{'))
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var timestamp = TryGetDateTimeOffset(root, "timestamp") ??
                TryGetDateTimeOffset(root, "Timestamp") ??
                TryGetDateTimeOffset(root, "@timestamp") ??
                fallbackTimestamp;
            var message = FirstNonEmpty(
                TryGetString(root, "message"),
                TryGetString(root, "Message"),
                TryGetString(root, "renderedMessage"),
                TryGetString(root, "body"),
                line) ?? line;
            var severity = FirstNonEmpty(
                TryGetString(root, "severity"),
                TryGetString(root, "Severity"),
                TryGetString(root, "logLevel"),
                TryGetString(root, "LogLevel"),
                TryGetString(root, "level"),
                fallbackSeverity);
            var source = FirstNonEmpty(
                TryGetString(root, "source"),
                TryGetString(root, "Source"),
                TryGetString(root, "sourceContext"),
                TryGetString(root, "SourceContext"),
                fallbackSource);
            var category = FirstNonEmpty(
                TryGetString(root, "category"),
                TryGetString(root, "Category"),
                TryGetString(root, "logger"),
                TryGetString(root, "Logger"),
                TryGetString(root, "loggerName"),
                TryGetString(root, "LoggerName"));
            var eventId = FirstNonEmpty(
                TryGetScalar(root, "eventId"),
                TryGetScalar(root, "EventId"),
                TryGetEventId(root, "EventId"),
                TryGetEventId(root, "eventId"));
            var traceId = FirstNonEmpty(
                TryGetString(root, "traceId"),
                TryGetString(root, "TraceId"));
            var spanId = FirstNonEmpty(
                TryGetString(root, "spanId"),
                TryGetString(root, "SpanId"));
            var exceptionSummary = FirstNonEmpty(
                TryGetString(root, "exceptionSummary"),
                TryGetString(root, "ExceptionSummary"),
                TryGetString(root, "exception"),
                TryGetString(root, "Exception"));

            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddStructuredAttributes(root, "attributes", attributes);
            AddStructuredAttributes(root, "Attributes", attributes);
            AddStructuredAttributes(root, "state", attributes);
            AddStructuredAttributes(root, "State", attributes);
            ReadScopeCorrelation(root, ref traceId, ref spanId, attributes);

            return new LogEntry(
                timestamp,
                message.TrimEnd(),
                severity,
                source,
                eventId,
                category,
                traceId,
                spanId,
                exceptionSummary,
                attributes.Count == 0 ? null : attributes);
        }
    }

    private static void ReadScopeCorrelation(
        JsonElement root,
        ref string? traceId,
        ref string? spanId,
        Dictionary<string, string> attributes)
    {
        foreach (var propertyName in new[] { "scopes", "Scopes" })
        {
            if (!TryGetProperty(root, propertyName, out var scopes) ||
                scopes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var index = 0;
            foreach (var scope in scopes.EnumerateArray())
            {
                if (scope.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                traceId = FirstNonEmpty(traceId, TryGetString(scope, "traceId"), TryGetString(scope, "TraceId"));
                spanId = FirstNonEmpty(spanId, TryGetString(scope, "spanId"), TryGetString(scope, "SpanId"));

                foreach (var property in scope.EnumerateObject())
                {
                    if (IsMessageProperty(property.Name) ||
                        IsCorrelationProperty(property.Name))
                    {
                        continue;
                    }

                    var value = GetScalarString(property.Value);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        attributes[$"scope.{index}.{NormalizeAttributeName(property.Name)}"] = value;
                    }
                }

                index++;
            }
        }
    }

    private static void AddStructuredAttributes(
        JsonElement root,
        string propertyName,
        Dictionary<string, string> attributes)
    {
        if (!TryGetProperty(root, propertyName, out var state) ||
            state.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in state.EnumerateObject())
        {
            var value = GetScalarString(property.Value);
            if (!string.IsNullOrWhiteSpace(value))
            {
                attributes[NormalizeAttributeName(property.Name)] = value;
            }
        }
    }

    private static string NormalizeAttributeName(string name) =>
        string.Equals(name, "{OriginalFormat}", StringComparison.Ordinal)
            ? "log.originalFormat"
            : name;

    private static bool IsMessageProperty(string name) =>
        string.Equals(name, "message", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "{OriginalFormat}", StringComparison.Ordinal);

    private static bool IsCorrelationProperty(string name) =>
        string.Equals(name, "traceId", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "spanId", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "parentId", StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset? TryGetDateTimeOffset(JsonElement root, string propertyName)
    {
        var value = TryGetString(root, propertyName);
        return DateTimeOffset.TryParse(value, out var timestamp)
            ? timestamp
            : null;
    }

    private static string? TryGetEventId(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var eventId) ||
            eventId.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return FirstNonEmpty(
            TryGetScalar(eventId, "name"),
            TryGetScalar(eventId, "Name"),
            TryGetScalar(eventId, "id"),
            TryGetScalar(eventId, "Id"));
    }

    private static string? TryGetString(JsonElement root, string propertyName) =>
        TryGetProperty(root, propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? TryGetScalar(JsonElement root, string propertyName) =>
        TryGetProperty(root, propertyName, out var property)
            ? GetScalarString(property)
            : null;

    private static string? GetScalarString(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement property)
    {
        if (root.TryGetProperty(propertyName, out property))
        {
            return true;
        }

        foreach (var candidate in root.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
