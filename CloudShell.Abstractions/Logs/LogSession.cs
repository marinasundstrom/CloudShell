namespace CloudShell.Abstractions.Logs;

/// <summary>
/// A source-addressed entry returned by a log session.
/// </summary>
public sealed record LogSessionEntry(
    string SourceId,
    LogEntry Entry);

/// <summary>
/// Selects the log sources that participate in one read and live stream.
/// </summary>
public sealed record LogSessionOptions(
    IReadOnlyList<string> SourceIds);

/// <summary>
/// Represents one consumer's access to one or more log sources.
/// </summary>
/// <remarks>
/// The manager coordinates access but does not record or retain log data.
/// <see cref="ReadAsync"/> returns a bounded snapshot from readable sources;
/// <see cref="StreamAsync"/> also follows sources that advertise streaming.
/// Reconnect cursors, query pushdown, and partial-source failure diagnostics
/// may extend the session shape.
/// </remarks>
public interface ILogSession : IAsyncDisposable
{
    string Id { get; }

    IReadOnlyList<string> SourceIds { get; }

    LogSourceSessionStatus Status { get; }

    Task<IReadOnlyList<LogSessionEntry>> ReadAsync(
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<LogSessionEntry> StreamAsync(
        int initialEntries = 50,
        CancellationToken cancellationToken = default);
}
