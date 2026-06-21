using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace CloudShell.Providers.Applications;

public sealed partial class LocalProcessRunner(
    ApplicationRuntimeStateStore runtimeStates,
    LocalProcessOptions options,
    IHostEnvironment environment,
    ILoggerFactory? loggerFactory = null) : IDisposable
{
    private readonly ConcurrentDictionary<string, LocalProcessState> _processes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _logger =
        loggerFactory?.CreateLogger(CloudShellLogCategories.LocalProcessLifecycle) ??
        NullLogger.Instance;

    public bool IsRunning(LocalProcessDefinition? definition) =>
        TryGetRunningProcess(definition, out _);

    public async Task<LocalProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        LocalProcessDefinition? definition,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRunningProcess(definition, out var process))
        {
            return null;
        }

        try
        {
            var firstTimestamp = DateTimeOffset.UtcNow;
            var firstProcessorTime = process.TotalProcessorTime;
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);

            process.Refresh();
            if (process.HasExited)
            {
                return null;
            }

            var timestamp = DateTimeOffset.UtcNow;
            var processorTime = process.TotalProcessorTime;
            var elapsed = timestamp - firstTimestamp;
            var cpuPercent = elapsed.TotalMilliseconds > 0
                ? (processorTime - firstProcessorTime).TotalMilliseconds /
                    (elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100
                : 0;

            return new LocalProcessMonitoringSnapshot(
                process.Id,
                TryGetStartTime(process),
                timestamp,
                Math.Max(0, cpuPercent),
                processorTime,
                process.WorkingSet64,
                process.PrivateMemorySize64,
                process.Threads.Count);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    public async Task StartAsync(
        LocalProcessDefinition definition,
        CancellationToken cancellationToken = default)
    {
        if (TryGetRunningProcess(definition, out _))
        {
            return;
        }

        var logPath = GetLogPath(definition.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var processLog = new ApplicationProcessLog(logPath);
        var startInfo = definition.Lifetime == LocalProcessLifetime.Detached
            ? CreateDetachedStartInfo(definition, logPath)
            : CreateScopedStartInfo(definition);
        var commandLine = FormatLocalProcessCommandLine(definition.ExecutablePath, definition.Arguments);

        foreach (var variable in definition.EnvironmentVariables ?? [])
        {
            if (string.IsNullOrWhiteSpace(variable.Name))
            {
                continue;
            }

            startInfo.Environment[variable.Name] = variable.Value;
        }

        startInfo.Environment["CLOUDSHELL_RESOURCE_ID"] = definition.Id;

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (definition.Lifetime == LocalProcessLifetime.ControlPlaneScoped)
        {
            process.OutputDataReceived += (_, args) => processLog.Append(args.Data ?? string.Empty, "stdout");
            process.ErrorDataReceived += (_, args) => processLog.Append(args.Data ?? string.Empty, "stderr", "Error");
        }

        process.Exited += (_, _) =>
        {
            var exitCode = process.ExitCode;
            processLog.Append(
                $"Process exited with code {exitCode}.",
                "process",
                exitCode == 0 ? "Information" : "Error");
            runtimeStates.Save(new ApplicationRuntimeState(
                definition.Id,
                process.Id,
                null,
                DateTimeOffset.UtcNow,
                exitCode,
                logPath));
            LogProcessExited(definition, process.Id, exitCode, commandLine);
        };

        cancellationToken.ThrowIfCancellationRequested();
        LogProcessStarting(definition, commandLine, startInfo.WorkingDirectory);
        process.Start();

        if (definition.Lifetime == LocalProcessLifetime.ControlPlaneScoped)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        var startedAt = TryGetStartTime(process);
        LogProcessStarted(definition, process.Id, commandLine, startInfo.WorkingDirectory);
        runtimeStates.Save(new ApplicationRuntimeState(
            definition.Id,
            process.Id,
            startedAt,
            DateTimeOffset.UtcNow,
            LogPath: logPath));

        processLog.Append(
            $"Started {commandLine} with process id {process.Id} using {definition.Lifetime} lifetime.",
            "process",
            "Information");

        _processes[definition.Id] = new LocalProcessState(
            process,
            processLog,
            definition.Lifetime,
            logPath);
    }

    public async Task<int> RunCommandAsync(
        string resourceId,
        string executablePath,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var logPath = GetLogPath(resourceId);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var processLog = new ApplicationProcessLog(logPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveExecutablePath(executablePath),
            WorkingDirectory = ResolveWorkingDirectory(workingDirectory),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var commandLine = FormatLocalProcessCommandLine(executablePath, arguments);
        processLog.Append(
            $"Running {commandLine} before starting the resource.",
            "process",
            "Information");
        LogPreStartCommandStarting(resourceId, commandLine, startInfo.WorkingDirectory);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                processLog.Append(
                    "Pre-start command could not be started.",
                    "process",
                    "Error");
                LogPreStartCommandCouldNotStart(resourceId, commandLine);
                return -1;
            }

            LogPreStartCommandStarted(resourceId, process.Id, commandLine, startInfo.WorkingDirectory);
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            var error = await errorTask;

            if (!string.IsNullOrWhiteSpace(output))
            {
                processLog.Append(output.Trim(), "stdout", "Information");
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                processLog.Append(error.Trim(), "stderr", process.ExitCode == 0 ? "Information" : "Error");
            }

            processLog.Append(
                $"Pre-start command exited with code {process.ExitCode}.",
                "process",
                process.ExitCode == 0 ? "Information" : "Error");
            LogPreStartCommandExited(resourceId, process.Id, commandLine, process.ExitCode);
            return process.ExitCode;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            processLog.Append(exception.Message, "process", "Error");
            LogPreStartCommandFailedToStart(resourceId, commandLine, exception);
            return -1;
        }
    }

    public Task StopAsync(
        LocalProcessDefinition definition,
        bool force = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var log = GetProcessLog(definition.Id);
        if (!TryGetRunningProcess(definition, out var process))
        {
            return Task.CompletedTask;
        }

        LogProcessStopping(definition, process.Id, force);
        log.Append(force ? "Stopping process." : "Stopping control-plane-scoped process.", "process", "Information");
        ProcessShutdown.KillProcessTreeAndWait(process);
        var exitCode = TryGetExitCode(process);
        runtimeStates.Save(new ApplicationRuntimeState(
            definition.Id,
            process.Id,
            null,
            DateTimeOffset.UtcNow,
            exitCode,
            GetLogPath(definition.Id)));
        LogProcessStopped(definition, process.Id, exitCode);
        return Task.CompletedTask;
    }

    public Task CleanupHostScopedProcessAsync(
        LocalProcessDefinition definition,
        CancellationToken cancellationToken = default) =>
        definition.Lifetime == LocalProcessLifetime.ControlPlaneScoped
            ? StopAsync(definition, force: false, cancellationToken)
            : Task.CompletedTask;

    public void Remove(string processId)
    {
        runtimeStates.Remove(processId);
    }

    public Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string processId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<LogEntry>>(
            GetProcessLog(processId).Read(maxEntries, before));
    }

    public async IAsyncEnumerable<LogEntry> StreamLogAsync(
        string processId,
        int initialEntries = 50,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var log = GetProcessLog(processId);
        foreach (var entry in log.Read(initialEntries, before: null))
        {
            yield return entry;
        }

        var seenEntries = log.CountEntries();
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            var entries = log.ReadAfter(seenEntries);
            seenEntries += entries.Count;
            foreach (var entry in entries)
            {
                yield return entry;
            }
        }
    }

    public void Dispose()
    {
        foreach (var (processId, state) in _processes)
        {
            try
            {
                if (state.Lifetime == LocalProcessLifetime.ControlPlaneScoped &&
                    !state.Process.HasExited)
                {
                    LogProcessStoppingDuringShutdown(processId, state.Process.Id);
                    ProcessShutdown.KillProcessTreeAndWait(state.Process);
                    var exitCode = TryGetExitCode(state.Process);
                    runtimeStates.Save(new ApplicationRuntimeState(
                        processId,
                        state.Process.Id,
                        null,
                        DateTimeOffset.UtcNow,
                        exitCode,
                        state.LogPath));
                    LogProcessStoppedDuringShutdown(processId, state.Process.Id, exitCode);
                }
            }
            catch (InvalidOperationException)
            {
            }

            state.Process.Dispose();
        }
    }

    private bool TryGetRunningProcess(
        LocalProcessDefinition? definition,
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
                state.LogPath));
            LogProcessExitObserved(definition, state.Process.Id, TryGetExitCode(state.Process));
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
                return false;
            }

            var logPath = runtimeState.LogPath ?? GetLogPath(definition.Id);
            var log = new ApplicationProcessLog(logPath);
            candidate.EnableRaisingEvents = true;
            candidate.Exited += (_, _) =>
            {
                var exitCode = TryGetExitCode(candidate);
                log.Append(
                    $"Process exited with code {exitCode?.ToString() ?? "unknown"}.",
                    "process",
                    exitCode == 0 ? "Information" : "Error");
                runtimeStates.Save(new ApplicationRuntimeState(
                    definition.Id,
                    candidate.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    exitCode,
                    logPath));
                LogProcessExited(
                    definition,
                    candidate.Id,
                    exitCode,
                    FormatLocalProcessCommandLine(definition.ExecutablePath, definition.Arguments));
            };

            _processes[definition.Id] = new LocalProcessState(
                candidate,
                log,
                definition.Lifetime,
                logPath);
            LogRecoveredProcessTracking(definition, candidate.Id, logPath);
            process = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private ApplicationProcessLog GetProcessLog(string processId)
    {
        if (_processes.TryGetValue(processId, out var state))
        {
            return state.Log;
        }

        return new ApplicationProcessLog(
            runtimeStates.Get(processId)?.LogPath ?? GetLogPath(processId));
    }

    private string GetLogPath(string processId)
    {
        var logDirectory = Path.IsPathRooted(options.LogDirectory)
            ? options.LogDirectory
            : Path.GetFullPath(options.LogDirectory, environment.ContentRootPath);
        var logFileName = SlugPattern()
            .Replace(processId.ToLowerInvariant(), "-")
            .Trim('-');

        return Path.Combine(logDirectory, $"{logFileName}.log");
    }

    private ProcessStartInfo CreateScopedStartInfo(LocalProcessDefinition definition) =>
        new()
        {
            FileName = ResolveExecutablePath(definition.ExecutablePath),
            Arguments = definition.Arguments ?? string.Empty,
            WorkingDirectory = ResolveWorkingDirectory(definition.WorkingDirectory),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

    private ProcessStartInfo CreateDetachedStartInfo(
        LocalProcessDefinition definition,
        string logPath)
    {
        var workingDirectory = ResolveWorkingDirectory(definition.WorkingDirectory);
        var executablePath = ResolveExecutablePath(definition.ExecutablePath);
        var arguments = definition.Arguments ?? string.Empty;

        if (OperatingSystem.IsWindows())
        {
            var command = $"\"{EscapeWindowsCommandArgument(executablePath)}\" {arguments} >> \"{EscapeWindowsCommandArgument(logPath)}\" 2>&1";
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(command);
            return startInfo;
        }

        var shellCommand = $"exec {QuoteUnixShellArgument(executablePath)} {arguments} >> {QuoteUnixShellArgument(logPath)} 2>&1";
        var unixStartInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        unixStartInfo.ArgumentList.Add("-c");
        unixStartInfo.ArgumentList.Add(shellCommand);
        return unixStartInfo;
    }

    private string ResolveWorkingDirectory(string? workingDirectory) =>
        string.IsNullOrWhiteSpace(workingDirectory)
            ? environment.ContentRootPath
            : Path.IsPathRooted(workingDirectory)
                ? workingDirectory
                : Path.GetFullPath(workingDirectory, environment.ContentRootPath);

    private static string ResolveExecutablePath(string executablePath)
    {
        if (!IsDotNetCommand(executablePath))
        {
            return executablePath;
        }

        var hostPath = AppContext.GetData("DOTNET_HOST_PATH") as string ??
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (IsUsableDotNetPath(hostPath))
        {
            return hostPath!;
        }

        var processPath = Environment.ProcessPath;
        if (IsUsableDotNetPath(processPath))
        {
            return processPath!;
        }

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            var rootDotNet = Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (IsUsableDotNetPath(rootDotNet))
            {
                return rootDotNet;
            }
        }

        return executablePath;
    }

    private static bool IsDotNetCommand(string executablePath) =>
        string.Equals(executablePath, "dotnet", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(executablePath, "dotnet.exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsUsableDotNetPath(string? executablePath) =>
        !string.IsNullOrWhiteSpace(executablePath) &&
        IsDotNetCommand(Path.GetFileName(executablePath)) &&
        File.Exists(executablePath);

    internal static string FormatLocalProcessCommandLine(
        string executablePath,
        string? arguments)
    {
        var executable = QuoteCommandArgument(executablePath);
        var trimmedArguments = string.IsNullOrWhiteSpace(arguments)
            ? string.Empty
            : arguments.Trim();
        return string.IsNullOrWhiteSpace(trimmedArguments)
            ? executable
            : $"{executable} {trimmedArguments}";
    }

    internal static string FormatLocalProcessCommandLine(
        string executablePath,
        IReadOnlyList<string> arguments)
    {
        var executable = QuoteCommandArgument(executablePath);
        return arguments.Count == 0
            ? executable
            : $"{executable} {string.Join(' ', arguments.Select(QuoteCommandArgument))}";
    }

    private void LogProcessStarting(
        LocalProcessDefinition definition,
        string commandLine,
        string workingDirectory)
    {
        using var scope = BeginResourceProcessScope(definition.Id);
        _logger.LogInformation(
            "Starting local process for resource {ResourceName}: {ProcessCommandLine} in {WorkingDirectory} with {ProcessLifetime} lifetime.",
            ResourceDisplayLabels.GetName(definition.Id),
            commandLine,
            workingDirectory,
            definition.Lifetime);
    }

    private void LogProcessStarted(
        LocalProcessDefinition definition,
        int processId,
        string commandLine,
        string workingDirectory)
    {
        using var scope = BeginResourceProcessScope(definition.Id, processId);
        _logger.LogInformation(
            "Started local process {ProcessId} for resource {ResourceName}: {ProcessCommandLine} in {WorkingDirectory}.",
            processId,
            ResourceDisplayLabels.GetName(definition.Id),
            commandLine,
            workingDirectory);
    }

    private void LogProcessExited(
        LocalProcessDefinition definition,
        int processId,
        int? exitCode,
        string commandLine)
    {
        using var scope = BeginResourceProcessScope(definition.Id, processId);
        _logger.LogInformation(
            "Local process {ProcessId} for resource {ResourceName} exited with code {ExitCode}: {ProcessCommandLine}.",
            processId,
            ResourceDisplayLabels.GetName(definition.Id),
            exitCode?.ToString() ?? "unknown",
            commandLine);
    }

    private void LogProcessStopping(
        LocalProcessDefinition definition,
        int processId,
        bool force)
    {
        using var scope = BeginResourceProcessScope(definition.Id, processId);
        _logger.LogInformation(
            "Stopping local process {ProcessId} for resource {ResourceName}; force={Force}.",
            processId,
            ResourceDisplayLabels.GetName(definition.Id),
            force);
    }

    private void LogProcessStopped(
        LocalProcessDefinition definition,
        int processId,
        int? exitCode)
    {
        using var scope = BeginResourceProcessScope(definition.Id, processId);
        _logger.LogInformation(
            "Stopped local process {ProcessId} for resource {ResourceName}; exit code {ExitCode}.",
            processId,
            ResourceDisplayLabels.GetName(definition.Id),
            exitCode?.ToString() ?? "unknown");
    }

    private void LogProcessStoppingDuringShutdown(string resourceId, int processId)
    {
        using var scope = BeginResourceProcessScope(resourceId, processId);
        _logger.LogInformation(
            "Stopping control-plane-scoped local process {ProcessId} for resource {ResourceName} during Control Plane shutdown.",
            processId,
            ResourceDisplayLabels.GetName(resourceId));
    }

    private void LogProcessStoppedDuringShutdown(
        string resourceId,
        int processId,
        int? exitCode)
    {
        using var scope = BeginResourceProcessScope(resourceId, processId);
        _logger.LogInformation(
            "Stopped control-plane-scoped local process {ProcessId} for resource {ResourceName} during Control Plane shutdown; exit code {ExitCode}.",
            processId,
            ResourceDisplayLabels.GetName(resourceId),
            exitCode?.ToString() ?? "unknown");
    }

    private void LogProcessExitObserved(
        LocalProcessDefinition definition,
        int processId,
        int? exitCode)
    {
        using var scope = BeginResourceProcessScope(definition.Id, processId);
        _logger.LogInformation(
            "Observed previously tracked local process {ProcessId} for resource {ResourceName} has exited with code {ExitCode}.",
            processId,
            ResourceDisplayLabels.GetName(definition.Id),
            exitCode?.ToString() ?? "unknown");
    }

    private void LogRecoveredProcessTracking(
        LocalProcessDefinition definition,
        int processId,
        string logPath)
    {
        using var scope = BeginResourceProcessScope(definition.Id, processId);
        _logger.LogInformation(
            "Recovered tracking for local process {ProcessId} under resource {ResourceName}; log path {LogPath}.",
            processId,
            ResourceDisplayLabels.GetName(definition.Id),
            logPath);
    }

    private void LogPreStartCommandStarting(
        string resourceId,
        string commandLine,
        string workingDirectory)
    {
        using var scope = BeginResourceProcessScope(resourceId);
        _logger.LogInformation(
            "Starting pre-start process for resource {ResourceName}: {ProcessCommandLine} in {WorkingDirectory}.",
            ResourceDisplayLabels.GetName(resourceId),
            commandLine,
            workingDirectory);
    }

    private void LogPreStartCommandStarted(
        string resourceId,
        int processId,
        string commandLine,
        string workingDirectory)
    {
        using var scope = BeginResourceProcessScope(resourceId, processId);
        _logger.LogInformation(
            "Started pre-start process {ProcessId} for resource {ResourceName}: {ProcessCommandLine} in {WorkingDirectory}.",
            processId,
            ResourceDisplayLabels.GetName(resourceId),
            commandLine,
            workingDirectory);
    }

    private void LogPreStartCommandExited(
        string resourceId,
        int processId,
        string commandLine,
        int exitCode)
    {
        using var scope = BeginResourceProcessScope(resourceId, processId);
        _logger.LogInformation(
            "Pre-start process {ProcessId} for resource {ResourceName} exited with code {ExitCode}: {ProcessCommandLine}.",
            processId,
            ResourceDisplayLabels.GetName(resourceId),
            exitCode,
            commandLine);
    }

    private void LogPreStartCommandCouldNotStart(string resourceId, string commandLine)
    {
        using var scope = BeginResourceProcessScope(resourceId);
        _logger.LogWarning(
            "Pre-start process for resource {ResourceName} could not be started: {ProcessCommandLine}.",
            ResourceDisplayLabels.GetName(resourceId),
            commandLine);
    }

    private void LogPreStartCommandFailedToStart(
        string resourceId,
        string commandLine,
        Exception exception)
    {
        using var scope = BeginResourceProcessScope(resourceId);
        _logger.LogWarning(
            exception,
            "Pre-start process for resource {ResourceName} failed to start: {ProcessCommandLine}.",
            ResourceDisplayLabels.GetName(resourceId),
            commandLine);
    }

    private IDisposable? BeginResourceProcessScope(string resourceId, int? processId = null)
    {
        var scope = new Dictionary<string, object?>
        {
            ["ResourceId"] = resourceId,
            ["ResourceName"] = ResourceDisplayLabels.GetName(resourceId)
        };
        if (processId is not null)
        {
            scope["ProcessId"] = processId.Value;
        }

        return _logger.BeginScope(scope);
    }

    private static string QuoteCommandArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        return argument.Any(char.IsWhiteSpace) || argument.Contains('"', StringComparison.Ordinal)
            ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : argument;
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

    private static DateTimeOffset? TryGetStartTime(Process process)
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

    private static int? TryGetExitCode(Process process)
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

    private static string QuoteUnixShellArgument(string value) =>
        $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private static string EscapeWindowsCommandArgument(string value) =>
        value.Replace("\"", "\\\"", StringComparison.Ordinal);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugPattern();

    private sealed record LocalProcessState(
        Process Process,
        ApplicationProcessLog Log,
        LocalProcessLifetime Lifetime,
        string LogPath);
}

public sealed record LocalProcessMonitoringSnapshot(
    int ProcessId,
    DateTimeOffset? StartedAt,
    DateTimeOffset Timestamp,
    double CpuUsagePercent,
    TimeSpan TotalProcessorTime,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    int ThreadCount);
