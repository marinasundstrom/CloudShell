using System.Runtime.CompilerServices;
using System.Text;
using CloudShell.Abstractions.Logs;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.Logs;

public sealed class FileLogProviderOptions
{
    public const string SectionName = "CloudShell:Logs:Files";

    /// <summary>
    /// Absolute directory paths from which explicitly declared file log sources may be opened.
    /// </summary>
    public List<string> AllowedRoots { get; set; } = [];

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public int MaxEntriesPerRead { get; set; } = 5_000;

    public int MaxSnapshotBytes { get; set; } = 1_048_576;

    public int MaxReadBytesPerPoll { get; set; } = 262_144;

    public int MaxLineLength { get; set; } = 65_536;
}

/// <summary>
/// Opens allowlisted, resource-declared UTF-8 log files without owning their recording or retention.
/// </summary>
public sealed class FileLogProvider(IOptions<FileLogProviderOptions> options) : ILogProvider
{
    private readonly FileLogProviderOptions options = options.Value;

    public string Id => "cloudshell.files";

    public string DisplayName => "Files";

    public IReadOnlyList<LogSource> GetLogSources() => [];

    public bool CanOpenLogSource(LogSource source) =>
        source.Kind == ResourceLogSourceKind.File &&
        source.SupportsReading &&
        TryResolveAuthorizedPath(source.Location, out _);

    public ValueTask<ILogSourceSession?> OpenLogSourceAsync(
        LogSource source,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanOpenLogSource(source) ||
            !TryResolveAuthorizedPath(source.Location, out var path))
        {
            return ValueTask.FromResult<ILogSourceSession?>(null);
        }

        var reader = new FileLogReader(path, source, options);
        return ValueTask.FromResult<ILogSourceSession?>(new DelegateLogSourceSession(
            source.Id,
            reader.ReadAsync,
            source.SupportsStreaming ? reader.StreamAsync : null));
    }

    private bool TryResolveAuthorizedPath(
        string? location,
        out AuthorizedFileLogPath path)
    {
        path = default;
        if (string.IsNullOrWhiteSpace(location) ||
            !Path.IsPathFullyQualified(location) ||
            options.AllowedRoots.Count == 0)
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(location);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        foreach (var configuredRoot in options.AllowedRoots)
        {
            if (string.IsNullOrWhiteSpace(configuredRoot) ||
                !Path.IsPathFullyQualified(configuredRoot))
            {
                continue;
            }

            string root;
            try
            {
                root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredRoot));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, fullPath);
            if (relative == "." ||
                Path.IsPathFullyQualified(relative) ||
                relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            path = new AuthorizedFileLogPath(root, fullPath);
            return true;
        }

        return false;
    }

    private readonly record struct AuthorizedFileLogPath(string Root, string Path);

    private sealed class FileLogReader(
        AuthorizedFileLogPath path,
        LogSource source,
        FileLogProviderOptions options)
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: false);
        private readonly TimeSpan pollInterval = options.PollInterval > TimeSpan.Zero
            ? options.PollInterval
            : TimeSpan.FromMilliseconds(250);
        private readonly int maxEntriesPerRead = Math.Clamp(options.MaxEntriesPerRead, 1, 10_000);
        private readonly int maxSnapshotBytes = Math.Clamp(options.MaxSnapshotBytes, 1_024, 16_777_216);
        private readonly int maxLineLength = Math.Clamp(options.MaxLineLength, 256, 1_048_576);
        private readonly int maxReadBytesPerPoll = Math.Max(
            Math.Clamp(options.MaxLineLength, 256, 1_048_576),
            Math.Clamp(options.MaxReadBytesPerPoll, 1_024, 4_194_304));

        public async Task<IReadOnlyList<LogEntry>> ReadAsync(
            int maxEntries,
            DateTimeOffset? before,
            CancellationToken cancellationToken)
        {
            var snapshot = await ReadSnapshotAsync(
                Math.Clamp(maxEntries, 1, maxEntriesPerRead),
                before,
                cancellationToken);
            return snapshot.Entries;
        }

        public async IAsyncEnumerable<LogEntry> StreamAsync(
            int initialEntries,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var snapshot = await ReadSnapshotAsync(
                Math.Clamp(initialEntries, 0, maxEntriesPerRead),
                before: null,
                cancellationToken);
            foreach (var entry in snapshot.Entries)
            {
                yield return entry;
            }

            var offset = snapshot.CompleteLength;
            var fingerprint = snapshot.Fingerprint;
            var discardingOversizedLine = false;

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(pollInterval, cancellationToken);
                var update = await ReadStreamUpdateAsync(
                    offset,
                    fingerprint,
                    discardingOversizedLine,
                    cancellationToken);
                offset = update.Offset;
                fingerprint = update.Fingerprint;
                discardingOversizedLine = update.DiscardingOversizedLine;
                foreach (var entry in update.Entries)
                {
                    yield return entry;
                }
            }
        }

        private async Task<FileStreamUpdate> ReadStreamUpdateAsync(
            long offset,
            byte[] fingerprint,
            bool discardingOversizedLine,
            CancellationToken cancellationToken)
        {
            if (!IsSafePath() || !File.Exists(path.Path))
            {
                return new([], offset, fingerprint, discardingOversizedLine);
            }

            try
            {
                await using var stream = OpenFile();
                var currentFingerprint = await ReadFingerprintAsync(stream, cancellationToken);
                if (stream.Length < offset ||
                    !FingerprintsAreCompatible(fingerprint, currentFingerprint))
                {
                    offset = 0;
                    discardingOversizedLine = false;
                }

                if (stream.Length <= offset)
                {
                    return new([], offset, currentFingerprint, discardingOversizedLine);
                }

                stream.Position = offset;
                var bytesToRead = (int)Math.Min(maxReadBytesPerPoll, stream.Length - offset);
                var buffer = new byte[bytesToRead];
                var bytesRead = await ReadAvailableAsync(stream, buffer, cancellationToken);
                if (bytesRead == 0)
                {
                    return new([], offset, currentFingerprint, discardingOversizedLine);
                }

                var data = buffer.AsSpan(0, bytesRead);
                if (discardingOversizedLine)
                {
                    var newline = data.IndexOf((byte)'\n');
                    return new(
                        [],
                        offset + (newline >= 0 ? newline + 1 : bytesRead),
                        currentFingerprint,
                        newline < 0);
                }

                var lastNewline = data.LastIndexOf((byte)'\n');
                if (lastNewline < 0)
                {
                    if (bytesRead < maxLineLength)
                    {
                        return new([], offset, currentFingerprint, false);
                    }

                    var message = DecodeLine(data[..Math.Min(maxLineLength, bytesRead)]) +
                        "… [line truncated]";
                    return new(
                        [Parse(message, DateTimeOffset.UtcNow)],
                        offset + bytesRead,
                        currentFingerprint,
                        true);
                }

                var complete = data[..(lastNewline + 1)];
                var entries = DecodeCompleteLines(complete)
                    .Select(line => Parse(Truncate(line), DateTimeOffset.UtcNow))
                    .ToArray();
                return new(
                    entries,
                    offset + lastNewline + 1,
                    currentFingerprint,
                    false);
            }
            catch (Exception exception) when (IsTransientFileException(exception))
            {
                return new([], offset, fingerprint, discardingOversizedLine);
            }
        }

        private async Task<FileSnapshot> ReadSnapshotAsync(
            int maxEntries,
            DateTimeOffset? before,
            CancellationToken cancellationToken)
        {
            if (!IsSafePath() || !File.Exists(path.Path))
            {
                return FileSnapshot.Empty;
            }

            try
            {
                await using var stream = OpenFile();
                var completeLength = await FindCompleteLengthAsync(stream, cancellationToken);
                var fingerprint = await ReadFingerprintAsync(stream, cancellationToken);
                if (completeLength == 0)
                {
                    return new([], 0, fingerprint);
                }

                if (maxEntries == 0)
                {
                    return new([], completeLength, fingerprint);
                }

                var start = Math.Max(0, completeLength - maxSnapshotBytes);
                var count = checked((int)(completeLength - start));
                stream.Position = start;
                var buffer = new byte[count];
                var bytesRead = await ReadAvailableAsync(stream, buffer, cancellationToken);
                var beginsAtLineBoundary = start == 0 ||
                    await ReadByteAtAsync(stream, start - 1, cancellationToken) == (byte)'\n';
                var data = buffer.AsSpan(0, bytesRead);
                if (!beginsAtLineBoundary)
                {
                    var firstNewline = data.IndexOf((byte)'\n');
                    data = firstNewline >= 0 ? data[(firstNewline + 1)..] : [];
                }

                var fallbackTimestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(path.Path));
                var entries = new Queue<LogEntry>(Math.Min(maxEntries, 256));
                foreach (var line in DecodeCompleteLines(data))
                {
                    var entry = Parse(Truncate(line), fallbackTimestamp);
                    if (before is not null && entry.Timestamp >= before.Value)
                    {
                        continue;
                    }

                    entries.Enqueue(entry);
                    if (entries.Count > maxEntries)
                    {
                        entries.Dequeue();
                    }
                }

                return new(entries.ToArray(), completeLength, fingerprint);
            }
            catch (Exception exception) when (IsTransientFileException(exception))
            {
                return FileSnapshot.Empty;
            }
        }

        private bool IsSafePath()
        {
            var relative = Path.GetRelativePath(path.Root, path.Path);
            var current = path.Root;
            foreach (var segment in relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (!File.Exists(current) && !Directory.Exists(current))
                {
                    continue;
                }

                try
                {
                    if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    {
                        return false;
                    }
                }
                catch (Exception exception) when (IsTransientFileException(exception))
                {
                    return false;
                }
            }

            return true;
        }

        private FileStream OpenFile() => new(
            path.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4_096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        private LogEntry Parse(string line, DateTimeOffset fallbackTimestamp) =>
            LogEntryParser.ParseLine(
                line,
                source.Name,
                severity: null,
                source.Format,
                fallbackTimestamp);

        private string Truncate(string line) =>
            line.Length <= maxLineLength
                ? line
                : line[..maxLineLength] + "… [line truncated]";

        private static IEnumerable<string> DecodeCompleteLines(ReadOnlySpan<byte> bytes)
        {
            var text = Utf8.GetString(bytes).TrimStart('\uFEFF');
            var lines = text.Split('\n', StringSplitOptions.None);
            return lines
                .Take(Math.Max(0, lines.Length - 1))
                .Select(line => line.TrimEnd('\r'));
        }

        private static string DecodeLine(ReadOnlySpan<byte> bytes) =>
            Utf8.GetString(bytes).TrimStart('\uFEFF').TrimEnd('\r', '\n');

        private async Task<long> FindCompleteLengthAsync(
            FileStream stream,
            CancellationToken cancellationToken)
        {
            var remaining = Math.Min(stream.Length, maxSnapshotBytes);
            var position = stream.Length;
            var buffer = new byte[Math.Min(4_096, maxSnapshotBytes)];
            while (remaining > 0)
            {
                var count = (int)Math.Min(buffer.Length, remaining);
                position -= count;
                stream.Position = position;
                var bytesRead = await ReadAvailableAsync(stream, buffer.AsMemory(0, count), cancellationToken);
                var newline = buffer.AsSpan(0, bytesRead).LastIndexOf((byte)'\n');
                if (newline >= 0)
                {
                    return position + newline + 1;
                }

                remaining -= bytesRead;
                if (bytesRead == 0)
                {
                    break;
                }
            }

            return 0;
        }

        private static async Task<byte[]> ReadFingerprintAsync(
            FileStream stream,
            CancellationToken cancellationToken)
        {
            stream.Position = 0;
            var fingerprint = new byte[(int)Math.Min(256, stream.Length)];
            var bytesRead = await ReadAvailableAsync(stream, fingerprint, cancellationToken);
            return bytesRead == fingerprint.Length ? fingerprint : fingerprint[..bytesRead];
        }

        private static bool FingerprintsAreCompatible(byte[] previous, byte[] current) =>
            previous.Length == 0 ||
            current.Length == 0 ||
            previous.AsSpan().StartsWith(current) ||
            current.AsSpan().StartsWith(previous);

        private static async Task<byte> ReadByteAtAsync(
            FileStream stream,
            long position,
            CancellationToken cancellationToken)
        {
            stream.Position = position;
            var buffer = new byte[1];
            return await ReadAvailableAsync(stream, buffer, cancellationToken) == 1 ? buffer[0] : (byte)0;
        }

        private static Task<int> ReadAvailableAsync(
            FileStream stream,
            byte[] buffer,
            CancellationToken cancellationToken) =>
            ReadAvailableAsync(stream, buffer.AsMemory(), cancellationToken);

        private static async Task<int> ReadAvailableAsync(
            FileStream stream,
            Memory<byte> buffer,
            CancellationToken cancellationToken)
        {
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer[total..], cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            return total;
        }

        private static bool IsTransientFileException(Exception exception) =>
            exception is IOException or UnauthorizedAccessException;

        private sealed record FileSnapshot(
            IReadOnlyList<LogEntry> Entries,
            long CompleteLength,
            byte[] Fingerprint)
        {
            public static FileSnapshot Empty { get; } = new([], 0, []);
        }

        private sealed record FileStreamUpdate(
            IReadOnlyList<LogEntry> Entries,
            long Offset,
            byte[] Fingerprint,
            bool DiscardingOversizedLine);
    }
}
