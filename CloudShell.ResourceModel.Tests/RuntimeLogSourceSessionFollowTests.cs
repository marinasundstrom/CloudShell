using CloudShell.Abstractions.Logs;
using CloudShell.ControlPlane.Providers;

namespace CloudShell.ResourceModel.Tests;

public sealed class RuntimeLogSourceSessionFollowTests
{
    [Fact]
    public async Task StreamAsync_FollowsEntriesAddedAfterTheInitialSnapshot()
    {
        var entries = new List<LogEntry>();
        var baselineRead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        Task<IReadOnlyList<LogEntry>> ReadAsync(
            int maxEntries,
            DateTimeOffset? before,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref reads) == 2)
            {
                baselineRead.TrySetResult();
            }

            lock (entries)
            {
                return Task.FromResult<IReadOnlyList<LogEntry>>(
                    entries
                        .Where(entry => before is null || entry.Timestamp < before)
                        .TakeLast(maxEntries)
                        .ToArray());
            }
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var stream = RuntimeLogSourceSessionFollow
            .StreamAsync(ReadAsync, 0, cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        var next = stream.MoveNextAsync().AsTask();
        await baselineRead.Task.WaitAsync(cancellation.Token);
        var expected = new LogEntry(
            DateTimeOffset.UtcNow,
            "live entry",
            "Information",
            "stdout");
        lock (entries)
        {
            entries.Add(expected);
        }

        Assert.True(await next);
        Assert.Equal(expected, stream.Current);
    }
}
