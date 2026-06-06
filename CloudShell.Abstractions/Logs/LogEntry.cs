namespace CloudShell.Abstractions.Logs;

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    string Message,
    string? Level = null,
    string? Source = null);
