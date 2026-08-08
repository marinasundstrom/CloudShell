using System.Globalization;
using System.Runtime.CompilerServices;
using CloudShell.Abstractions.Logs;

namespace CloudShell.ControlPlane.Providers;

internal static class RuntimeLogSourceSessionFollow
{
    public static async IAsyncEnumerable<LogEntry> StreamAsync(
        Func<int, DateTimeOffset?, CancellationToken, Task<IReadOnlyList<LogEntry>>> read,
        int initialEntries,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var initial = await read(initialEntries, null, cancellationToken);
        foreach (var entry in initial)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }

        var observed = (await read(1_000, null, cancellationToken))
            .Select(CreateEntryIdentity)
            .ToHashSet(StringComparer.Ordinal);
        while (true)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            var current = await read(1_000, null, cancellationToken);
            foreach (var entry in current)
            {
                if (observed.Add(CreateEntryIdentity(entry)))
                {
                    yield return entry;
                }
            }

            observed = current
                .Select(CreateEntryIdentity)
                .ToHashSet(StringComparer.Ordinal);
        }
    }

    private static string CreateEntryIdentity(LogEntry entry) =>
        string.Join(
            '\n',
            entry.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            entry.Source,
            entry.Severity,
            entry.Message);
}
