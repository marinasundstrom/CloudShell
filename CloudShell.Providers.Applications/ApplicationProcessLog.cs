using CloudShell.Abstractions.Logs;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationProcessLog
{
    private const int MaxEntries = 1_000;
    private readonly Queue<LogEntry> _entries = new();

    public void Append(string message, string source, string? level = null)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        lock (_entries)
        {
            _entries.Enqueue(new LogEntry(DateTimeOffset.UtcNow, message, level, source));
            while (_entries.Count > MaxEntries)
            {
                _entries.Dequeue();
            }
        }
    }

    public IReadOnlyList<LogEntry> Read(int maxEntries, DateTimeOffset? before)
    {
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
}
