using CloudShell.Abstractions.Logs;
using CloudShell.ControlPlane.Logs;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.Tests;

public sealed class FileLogProviderTests
{
    [Fact]
    public async Task ReadAsync_ReturnsBoundedCompleteStructuredEntries()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "application.log");
        await File.WriteAllTextAsync(
            path,
            "{\"Timestamp\":\"2026-08-08T10:00:00Z\",\"LogLevel\":\"Information\",\"Message\":\"first\"}\n" +
            "{\"Timestamp\":\"2026-08-08T10:00:01Z\",\"LogLevel\":\"Warning\",\"Message\":\"second\"}\n" +
            "{\"Timestamp\":\"2026-08-08T10:00:02Z\",\"LogLevel\":\"Error\",\"Message\":\"third\"}\n" +
            "incomplete");
        var provider = CreateProvider(directory.Path);
        var source = CreateSource(path, LogFormat.JsonConsole);

        await using var session = await provider.OpenLogSourceAsync(source);
        Assert.NotNull(session);

        var entries = await session.ReadAsync(maxEntries: 2);

        Assert.Collection(
            entries,
            entry =>
            {
                Assert.Equal("second", entry.Message);
                Assert.Equal("Warning", entry.Severity);
            },
            entry =>
            {
                Assert.Equal("third", entry.Message);
                Assert.Equal("Error", entry.Severity);
            });

        var older = await session.ReadAsync(
            maxEntries: 10,
            before: DateTimeOffset.Parse("2026-08-08T10:00:02Z"));
        Assert.Equal(["first", "second"], older.Select(entry => entry.Message));
    }

    [Fact]
    public async Task StreamAsync_WaitsForCompleteAppendedLine()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "application.log");
        await File.WriteAllTextAsync(path, "existing\n");
        var provider = CreateProvider(directory.Path, pollInterval: TimeSpan.FromMilliseconds(25));
        var source = CreateSource(path);
        await using var session = await provider.OpenLogSourceAsync(source);
        Assert.NotNull(session);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var stream = session.StreamAsync(0, cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        var next = stream.MoveNextAsync().AsTask();
        await File.AppendAllTextAsync(path, "partial", cancellation.Token);
        await Task.Delay(150, cancellation.Token);
        Assert.False(next.IsCompleted);

        await File.AppendAllTextAsync(path, " line\n", cancellation.Token);

        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(2), cancellation.Token));
        Assert.Equal("partial line", stream.Current.Message);
    }

    [Fact]
    public async Task StreamAsync_ResetsAfterFileTruncation()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "application.log");
        await File.WriteAllTextAsync(path, "old entry with a longer payload\n");
        var provider = CreateProvider(directory.Path, pollInterval: TimeSpan.FromMilliseconds(25));
        var source = CreateSource(path);
        await using var session = await provider.OpenLogSourceAsync(source);
        Assert.NotNull(session);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var stream = session.StreamAsync(0, cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        var next = stream.MoveNextAsync().AsTask();
        await Task.Delay(75, cancellation.Token);
        await File.WriteAllTextAsync(path, "rotated\n", cancellation.Token);

        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(2), cancellation.Token));
        Assert.Equal("rotated", stream.Current.Message);
    }

    [Fact]
    public async Task StreamAsync_FollowsFileCreatedAfterSessionOpens()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "created-later.log");
        var provider = CreateProvider(directory.Path, pollInterval: TimeSpan.FromMilliseconds(25));
        await using var session = await provider.OpenLogSourceAsync(CreateSource(path));
        Assert.NotNull(session);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var stream = session.StreamAsync(0, cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        var next = stream.MoveNextAsync().AsTask();
        await Task.Delay(75, cancellation.Token);
        await File.WriteAllTextAsync(path, "created\n", cancellation.Token);

        Assert.True(await next.WaitAsync(TimeSpan.FromSeconds(2), cancellation.Token));
        Assert.Equal("created", stream.Current.Message);
    }

    [Fact]
    public async Task OpenLogSourceAsync_RejectsPathsOutsideAllowedRoots()
    {
        using var allowed = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var path = Path.Combine(outside.Path, "application.log");
        await File.WriteAllTextAsync(path, "secret\n");
        var provider = CreateProvider(allowed.Path);

        var session = await provider.OpenLogSourceAsync(CreateSource(path));

        Assert.Null(session);
    }

    [Fact]
    public async Task ReadAsync_RejectsSymlinkTraversalInsideAllowedRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var allowed = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var outsidePath = Path.Combine(outside.Path, "application.log");
        await File.WriteAllTextAsync(outsidePath, "secret\n");
        var link = Path.Combine(allowed.Path, "linked.log");
        File.CreateSymbolicLink(link, outsidePath);
        var provider = CreateProvider(allowed.Path);
        await using var session = await provider.OpenLogSourceAsync(CreateSource(link));
        Assert.NotNull(session);

        var entries = await session.ReadAsync();

        Assert.Empty(entries);
    }

    private static FileLogProvider CreateProvider(
        string allowedRoot,
        TimeSpan? pollInterval = null) =>
        new(Options.Create(new FileLogProviderOptions
        {
            AllowedRoots = [allowedRoot],
            PollInterval = pollInterval ?? TimeSpan.FromMilliseconds(50)
        }));

    private static LogSource CreateSource(
        string path,
        LogFormat format = LogFormat.PlainText) =>
        new(
            "application:api:log-source:file",
            "Application file",
            "applications.test",
            "api",
            LogSourceKind.Resource,
            ResourceLogSourceKind.File,
            format,
            new LogStorage(LogStorageKind.File),
            LogSourceCapabilities.Read | LogSourceCapabilities.Stream,
            ResourceId: "application:api",
            Location: path,
            Availability: LogSourceAvailability.Persisted);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory("cloudshell-file-log-tests-").FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
