using CloudShell.Abstractions.Logs;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationProcessLog
{
    private const int MaxEntries = 1_000;
    private readonly Queue<LogEntry> _entries = new();
    private readonly string? _logPath;

    public ApplicationProcessLog(string? logPath = null)
    {
        _logPath = logPath;
    }

    public void Append(string message, string source, string? level = null)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        var entry = new LogEntry(DateTimeOffset.UtcNow, message, level, source);
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
        var level = string.IsNullOrWhiteSpace(entry.Level) ? "Information" : entry.Level;
        var source = string.IsNullOrWhiteSpace(entry.Source) ? "process" : entry.Source;
        using var stream = new FileStream(
            _logPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.WriteLine($"[{entry.Timestamp:O}] [{level}] [{source}] {entry.Message}");
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
                var level = ParseBracketedValue(ref remaining);
                var source = ParseBracketedValue(ref remaining);
                return new LogEntry(timestamp, remaining, level, source);
            }
        }

        return new LogEntry(DateTimeOffset.UtcNow, line, Source: "stdout");
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
}
