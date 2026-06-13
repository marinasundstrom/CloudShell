namespace CloudShell.Abstractions.Logs;

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    string Message,
    string? Level = null,
    string? Source = null,
    string? EventId = null,
    string? Category = null,
    string? TraceId = null,
    string? SpanId = null,
    string? ExceptionSummary = null,
    IReadOnlyDictionary<string, string>? Attributes = null);
