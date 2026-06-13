using CloudShell.Abstractions.Logs;
using System.Text.Json;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationProcessLog
{
    private const int MaxEntries = 1_000;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly Queue<LogEntry> _entries = new();
    private readonly string? _logPath;

    public ApplicationProcessLog(string? logPath = null)
    {
        _logPath = logPath;
    }

    public void Append(string message, string source, string? severity = null)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        var entry = ParseEntry(message, source, severity, DateTimeOffset.UtcNow);
        lock (_entries)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > MaxEntries)
            {
                _entries.Dequeue();
            }
        }

        AppendToFile(entry);
    }

    public IReadOnlyList<LogEntry> Read(int maxEntries, DateTimeOffset? before)
    {
        if (!string.IsNullOrWhiteSpace(_logPath) && File.Exists(_logPath))
        {
            return ReadFromFile(_logPath, maxEntries, before);
        }

        lock (_entries)
        {
            var query = _entries.AsEnumerable();
            if (before is not null)
            {
                query = query.Where(entry => entry.Timestamp < before.Value);
            }

            return query
                .TakeLast(Math.Max(1, maxEntries))
                .ToArray();
        }
    }

    public int CountEntries()
    {
        if (!string.IsNullOrWhiteSpace(_logPath) && File.Exists(_logPath))
        {
            return ReadAllLines(_logPath).Count;
        }

        lock (_entries)
        {
            return _entries.Count;
        }
    }

    public IReadOnlyList<LogEntry> ReadAfter(int entryIndex)
    {
        if (!string.IsNullOrWhiteSpace(_logPath) && File.Exists(_logPath))
        {
            return ReadAllLines(_logPath)
                .Skip(Math.Max(0, entryIndex))
                .Select(ParseLine)
                .ToArray();
        }

        lock (_entries)
        {
            return _entries
                .Skip(Math.Max(0, entryIndex))
                .ToArray();
        }
    }

    private void AppendToFile(LogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(_logPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        var severity = string.IsNullOrWhiteSpace(entry.Severity) ? "Information" : entry.Severity;
        var source = string.IsNullOrWhiteSpace(entry.Source) ? "process" : entry.Source;
        using var stream = new FileStream(
            _logPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        if (HasStructuredMetadata(entry))
        {
            writer.WriteLine(JsonSerializer.Serialize(CreateStructuredLogLine(entry, severity, source), SerializerOptions));
            return;
        }

        writer.WriteLine($"[{entry.Timestamp:O}] [{severity}] [{source}] {entry.Message}");
    }

    private static IReadOnlyList<LogEntry> ReadFromFile(
        string logPath,
        int maxEntries,
        DateTimeOffset? before)
    {
        var entries = ReadAllLines(logPath)
            .Select(ParseLine)
            .Where(entry => before is null || entry.Timestamp < before.Value)
            .TakeLast(Math.Max(1, maxEntries))
            .ToArray();

        return entries;
    }

    private static IReadOnlyList<string> ReadAllLines(string logPath)
    {
        using var stream = new FileStream(
            logPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static LogEntry ParseLine(string line)
    {
        if (line.StartsWith('['))
        {
            var timestampEnd = line.IndexOf(']');
            if (timestampEnd > 1 &&
                DateTimeOffset.TryParse(line[1..timestampEnd], out var timestamp))
            {
                var remaining = line[(timestampEnd + 1)..].TrimStart();
                var severity = ParseBracketedValue(ref remaining);
                var source = ParseBracketedValue(ref remaining);
                return ParseEntry(remaining, source, severity, timestamp);
            }
        }

        return ParseEntry(line, "stdout", null, DateTimeOffset.UtcNow);
    }

    private static string? ParseBracketedValue(ref string value)
    {
        if (!value.StartsWith('['))
        {
            return null;
        }

        var end = value.IndexOf(']');
        if (end <= 1)
        {
            return null;
        }

        var parsed = value[1..end];
        value = value[(end + 1)..].TrimStart();
        return parsed;
    }

    private static LogEntry ParseEntry(
        string message,
        string? source,
        string? severity,
        DateTimeOffset timestamp) =>
        TryParseStructuredJsonLog(message, source, severity, timestamp)
        ?? new LogEntry(timestamp, message, severity, source);

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

    private static bool HasStructuredMetadata(LogEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.EventId) ||
        !string.IsNullOrWhiteSpace(entry.Category) ||
        !string.IsNullOrWhiteSpace(entry.TraceId) ||
        !string.IsNullOrWhiteSpace(entry.SpanId) ||
        !string.IsNullOrWhiteSpace(entry.ExceptionSummary) ||
        entry.Attributes?.Count > 0;

    private static StructuredLogLine CreateStructuredLogLine(
        LogEntry entry,
        string severity,
        string source) =>
        new(
            entry.Timestamp,
            entry.Message,
            severity,
            source,
            entry.EventId,
            entry.Category,
            entry.TraceId,
            entry.SpanId,
            entry.ExceptionSummary,
            entry.Attributes);

    private sealed record StructuredLogLine(
        DateTimeOffset Timestamp,
        string Message,
        string Severity,
        string Source,
        string? EventId,
        string? Category,
        string? TraceId,
        string? SpanId,
        string? ExceptionSummary,
        IReadOnlyDictionary<string, string>? Attributes);
}
