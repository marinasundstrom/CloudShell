using System.Runtime.CompilerServices;
using System.Threading.Channels;
using CloudShell.Abstractions.Logs;

namespace CloudShell.ControlPlane.Logs;

internal sealed class CompositeLogSession(
    IReadOnlyList<CompositeLogSession.SourceSession> sources) : ILogSession
{
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private int status = (int)LogSourceSessionStatus.Active;

    public string Id { get; } = Guid.NewGuid().ToString("N");

    public IReadOnlyList<string> SourceIds { get; } = sources
        .Select(source => source.Source.Id)
        .ToArray();

    public LogSourceSessionStatus Status => (LogSourceSessionStatus)Volatile.Read(ref status);

    public async Task<IReadOnlyList<LogSessionEntry>> ReadAsync(
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Status == LogSourceSessionStatus.Closed, this);
        if (maxEntries <= 0)
        {
            return [];
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            lifetimeCancellation.Token,
            cancellationToken);
        var reads = await Task.WhenAll(sources.Select(async source =>
            (source.Source.Id, Entries: await source.Session.ReadAsync(
                maxEntries,
                before,
                linkedCancellation.Token))));

        return reads
            .SelectMany(read => read.Entries.Select(entry => new LogSessionEntry(read.Id, entry)))
            .OrderBy(entry => entry.Entry.Timestamp)
            .TakeLast(maxEntries)
            .ToArray();
    }

    public async IAsyncEnumerable<LogSessionEntry> StreamAsync(
        int initialEntries = 50,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Status == LogSourceSessionStatus.Closed, this);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            lifetimeCancellation.Token,
            cancellationToken);

        if (initialEntries > 0)
        {
            foreach (var entry in await ReadAsync(initialEntries, cancellationToken: linkedCancellation.Token))
            {
                yield return entry;
            }
        }

        var streamableSources = sources
            .Where(source => source.Source.SupportsStreaming)
            .ToArray();
        if (streamableSources.Length == 0)
        {
            yield break;
        }

        var channel = Channel.CreateBounded<LogSessionEntry>(new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        var pumps = streamableSources
            .Select(source => PumpAsync(source, channel.Writer, linkedCancellation.Token))
            .ToArray();
        var completion = CompleteAsync(pumps, channel.Writer, linkedCancellation);
        try
        {
            await foreach (var entry in channel.Reader.ReadAllAsync(linkedCancellation.Token))
            {
                yield return entry;
            }
        }
        finally
        {
            await linkedCancellation.CancelAsync();
            try
            {
                await completion;
            }
            catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if ((LogSourceSessionStatus)Interlocked.Exchange(
                ref status,
                (int)LogSourceSessionStatus.Closed) == LogSourceSessionStatus.Closed)
        {
            return;
        }

        lifetimeCancellation.Cancel();
        foreach (var source in sources)
        {
            await source.Session.DisposeAsync();
        }
        lifetimeCancellation.Dispose();
    }

    private static async Task PumpAsync(
        SourceSession source,
        ChannelWriter<LogSessionEntry> writer,
        CancellationToken cancellationToken)
    {
        await foreach (var entry in source.Session.StreamAsync(0, cancellationToken))
        {
            await writer.WriteAsync(new LogSessionEntry(source.Source.Id, entry), cancellationToken);
        }
    }

    private static async Task CompleteAsync(
        Task[] pumps,
        ChannelWriter<LogSessionEntry> writer,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.WhenAll(pumps);
            writer.TryComplete();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            writer.TryComplete();
        }
        catch (Exception exception)
        {
            cancellation.Cancel();
            writer.TryComplete(exception);
        }
    }

    internal sealed record SourceSession(LogSource Source, ILogSourceSession Session);
}
