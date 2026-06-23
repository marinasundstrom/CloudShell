using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CloudShell.Providers.Applications;

internal sealed partial class ApplicationContainerProcessTracker(
    ApplicationRuntimeStateStore runtimeStates,
    ApplicationProviderOptions options,
    IHostEnvironment environment,
    ILoggerFactory? loggerFactory = null) : IDisposable
{
    private readonly ConcurrentDictionary<string, ApplicationProcessState> _processes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger logger =
        loggerFactory?.CreateLogger(CloudShellLogCategories.DockerHostLifecycle) ??
        NullLogger.Instance;

    public bool IsRunning(ApplicationResourceDefinition? definition) =>
        TryGetRunningProcess(definition, out _);

    public bool TryGetRunningProcess(
        ApplicationResourceDefinition? definition,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Process? process)
    {
        process = null;
        if (definition is null)
        {
            return false;
        }

        if (_processes.TryGetValue(definition.Id, out var state))
        {
            if (!state.Process.HasExited)
            {
                process = state.Process;
                return true;
            }

            runtimeStates.Save(new ApplicationRuntimeState(
                definition.Id,
                state.Process.Id,
                null,
                DateTimeOffset.UtcNow,
                TryGetExitCode(state.Process),
                state.LogPath,
                VolumeMounts: MarkVolumeMountsNotActive(
                    runtimeStates.Get(definition.Id)?.RuntimeVolumeMounts ?? [],
                    DateTimeOffset.UtcNow)));
            if (_processes.TryRemove(definition.Id, out var removedState))
            {
                ReleaseTrackedApplicationProcess(definition.Id, removedState);
            }
        }

        var runtimeState = runtimeStates.Get(definition.Id);
        if (runtimeState?.LastKnownProcessId is null ||
            runtimeState.LastKnownProcessStartedAt is null)
        {
            return false;
        }

        try
        {
            var candidate = Process.GetProcessById(runtimeState.LastKnownProcessId.Value);
            if (candidate.HasExited ||
                !ProcessStartMatches(candidate, runtimeState.LastKnownProcessStartedAt.Value))
            {
                candidate.Dispose();
                return false;
            }

            var logPath = runtimeState.LogPath ?? GetLogPath(definition.Id);
            var log = CreateProcessLog(logPath);
            candidate.EnableRaisingEvents = true;
            candidate.Exited += (_, _) =>
            {
                log.Append(
                    $"Process exited with code {TryGetExitCode(candidate)?.ToString() ?? "unknown"}.",
                    "process",
                    TryGetExitCode(candidate) == 0 ? "Information" : "Error");
                runtimeStates.Save(new ApplicationRuntimeState(
                    definition.Id,
                    candidate.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    TryGetExitCode(candidate),
                    logPath,
                    VolumeMounts: MarkVolumeMountsNotActive(
                        runtimeStates.Get(definition.Id)?.RuntimeVolumeMounts ?? [],
                        DateTimeOffset.UtcNow)));
            };

            _processes[definition.Id] = new ApplicationProcessState(
                candidate,
                log,
                definition.Lifetime,
                logPath);
            process = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    public ApplicationProcessLog CreateProcessLogForStart(
        string applicationId,
        out string? logPath)
    {
        logPath = GetLogPath(applicationId);
        EnsureLogDirectory(logPath);
        return CreateProcessLog(logPath);
    }

    public ApplicationProcessLog GetProcessLog(string applicationId)
    {
        if (_processes.TryGetValue(applicationId, out var state))
        {
            return state.Log;
        }

        return CreateProcessLog(runtimeStates.Get(applicationId)?.LogPath ?? GetLogPath(applicationId));
    }

    public string? GetTrackedLogPath(string applicationId) =>
        _processes.TryGetValue(applicationId, out var state)
            ? state.LogPath
            : runtimeStates.Get(applicationId)?.LogPath ?? GetLogPath(applicationId);

    public void Track(
        string applicationId,
        Process process,
        ApplicationProcessLog processLog,
        ApplicationLifetime lifetime,
        string? logPath) =>
        _processes[applicationId] = new ApplicationProcessState(
            process,
            processLog,
            lifetime,
            logPath);

    public void Dispose()
    {
        foreach (var (applicationId, state) in _processes)
        {
            try
            {
                if (state.Lifetime == ApplicationLifetime.ControlPlaneScoped &&
                    !state.Process.HasExited)
                {
                    ProcessShutdown.KillProcessTreeAndWait(state.Process);
                    runtimeStates.Save(new ApplicationRuntimeState(
                        applicationId,
                        state.Process.Id,
                        null,
                        DateTimeOffset.UtcNow,
                        TryGetExitCode(state.Process),
                        state.LogPath,
                        VolumeMounts: MarkVolumeMountsNotActive(
                            runtimeStates.Get(applicationId)?.RuntimeVolumeMounts ?? [],
                            DateTimeOffset.UtcNow)));
                }
            }
            catch (InvalidOperationException)
            {
            }

            ReleaseTrackedApplicationProcess(applicationId, state);
        }
    }

    public static IReadOnlyList<ResourceVolumeMountMaterialization> MarkVolumeMountsNotActive(
        IEnumerable<ResourceVolumeMountMaterialization> mounts,
        DateTimeOffset observedAt) =>
        mounts
            .Select(mount => mount with
            {
                Status = ResourceVolumeMountMaterializationStatus.NotActive,
                ObservedAt = observedAt
            })
            .ToArray();

    public static int? TryGetProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static DateTimeOffset? TryGetStartTime(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void ReleaseTrackedApplicationProcess(
        string applicationId,
        ApplicationProcessState state)
    {
        var processId = TryGetProcessId(state.Process);
        logger.LogDebug(
            "Application provider released tracked {ApplicationLifetime} process handle {ProcessId} for resource {ResourceName}.",
            state.Lifetime,
            processId?.ToString(CultureInfo.InvariantCulture) ?? "detached",
            ResourceDisplayLabels.GetName(applicationId));
        state.Process.Dispose();
    }

    private ApplicationProcessLog CreateProcessLog(string? logPath) =>
        new(
            logPath,
            options.LogRetentionDays,
            options.RetainedLogEntries,
            options.SplitLogFilesByDay);

    private string? GetLogPath(string applicationId)
    {
        if (!IsFileLogStore())
        {
            return null;
        }

        return GetPersistedLogPath(applicationId);
    }

    private string GetPersistedLogPath(string applicationId)
    {
        var logDirectory = Path.IsPathRooted(options.LogDirectory)
            ? options.LogDirectory
            : Path.GetFullPath(options.LogDirectory, environment.ContentRootPath);
        var logFileName = SlugPattern()
            .Replace(applicationId.ToLowerInvariant(), "-")
            .Trim('-');

        return Path.Combine(logDirectory, $"{logFileName}.log");
    }

    private bool IsFileLogStore() =>
        string.Equals(options.LogStore, ApplicationLogStores.File, StringComparison.OrdinalIgnoreCase);

    private static void EnsureLogDirectory(string? logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
    }

    private static bool ProcessStartMatches(
        Process process,
        DateTimeOffset expectedStartedAt)
    {
        var actualStartedAt = TryGetStartTime(process);
        if (actualStartedAt is null)
        {
            return true;
        }

        return (actualStartedAt.Value - expectedStartedAt).Duration() <= TimeSpan.FromSeconds(2);
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugPattern();

    private sealed record ApplicationProcessState(
        Process Process,
        ApplicationProcessLog Log,
        ApplicationLifetime Lifetime,
        string? LogPath);
}
