using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CloudShell.ControlPlane.Providers;

public interface IGoAppRuntimeController
{
    GoAppRuntimeStatus GetStatus(Resource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);
}

public interface IGoAppRuntimeOutputReader
{
    IReadOnlyList<GoAppRuntimeOutputEntry> ReadOutput(
        string resourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null);
}

public interface IGoAppRuntimeMonitor
{
    ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        string resourceId,
        CancellationToken cancellationToken = default);
}

public interface IGoAppRuntimeEnvironmentProvider
{
    ValueTask<IReadOnlyDictionary<string, string>> ResolveAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed record GoAppRuntimeOutputEntry(
    DateTimeOffset Timestamp,
    string Message,
    string Stream,
    string? Severity = null);

public enum GoAppRuntimeStatus
{
    Unknown,
    Stopped,
    Running
}

public sealed class GoAppProcessRuntimeController(
    IEnumerable<IGoAppRuntimeEnvironmentProvider>? environmentProviders = null) :
    IGoAppRuntimeController,
    IGoAppRuntimeOutputReader,
    IGoAppRuntimeMonitor,
    IDisposable,
    IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Process> _processes = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BoundedRuntimeOutputBuffer> _output = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly GoAppProcessCommandFactory _commands = new();
    private readonly IReadOnlyList<IGoAppRuntimeEnvironmentProvider> _environmentProviders =
        (environmentProviders ?? []).ToArray();

    public GoAppRuntimeStatus GetStatus(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return _processes.TryGetValue(resource.EffectiveResourceId, out var process) &&
            !process.HasExited
                ? GoAppRuntimeStatus.Running
                : GoAppRuntimeStatus.Stopped;
    }

    public async ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        if (!_processes.TryGetValue(resourceId, out var process) ||
            process.HasExited)
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

            return new ResourceProcessMonitoringSnapshot(
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

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (operationId == GoAppResourceTypeProvider.Operations.Start)
        {
            return await StartAsync(resource, cancellationToken);
        }

        if (operationId == GoAppResourceTypeProvider.Operations.Stop)
        {
            await StopAsync(resource, cancellationToken);
            return [];
        }

        if (operationId == GoAppResourceTypeProvider.Operations.Restart)
        {
            await StopAsync(resource, cancellationToken);
            return await StartAsync(resource, cancellationToken);
        }

        return
        [
            ResourceDefinitionDiagnostic.Error(
                "application.goApp.operationUnsupported",
                $"Go app runtime does not support operation '{operationId}'.",
                resource.EffectiveResourceId)
        ];
    }

    private async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StartAsync(
        Resource resource,
        CancellationToken cancellationToken)
    {
        if (_processes.TryGetValue(resource.EffectiveResourceId, out var current) &&
            !current.HasExited)
        {
            return [];
        }

        var projectPath = resource.Attributes.GetString(
            GoAppResourceTypeProvider.Attributes.ProjectPath);
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.goApp.pathRequired",
                    "Go app project path is required.",
                    GoAppResourceTypeProvider.Attributes.ProjectPath)
            ];
        }

        var fullProjectPath = Path.GetFullPath(projectPath, Directory.GetCurrentDirectory());
        if (!Directory.Exists(fullProjectPath))
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.goApp.projectDirectoryMissing",
                    $"Go app project directory '{fullProjectPath}' does not exist.",
                    resource.EffectiveResourceId)
            ];
        }

        var binaryPath = resource.Attributes.GetString(
            GoAppResourceTypeProvider.Attributes.BinaryPath);
        if (!string.IsNullOrWhiteSpace(binaryPath))
        {
            var effectiveBinaryPath = Path.IsPathRooted(binaryPath)
                ? binaryPath.Trim()
                : Path.GetFullPath(binaryPath.Trim(), fullProjectPath);
            if (!File.Exists(effectiveBinaryPath))
            {
                return
                [
                    ResourceDefinitionDiagnostic.Error(
                        "application.goApp.binaryMissing",
                        $"Go app binary '{effectiveBinaryPath}' does not exist.",
                        GoAppResourceTypeProvider.Attributes.BinaryPath)
                ];
            }
        }

        var output = _output.GetOrAdd(
            resource.EffectiveResourceId,
            _ => new BoundedRuntimeOutputBuffer());
        output.Clear();

        var derivedEnvironmentVariables =
            await ResolveRuntimeEnvironmentVariablesAsync(resource, cancellationToken);
        var startInfo = _commands.CreateStartInfo(
            resource,
            fullProjectPath,
            derivedEnvironmentVariables);
        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, data) =>
            AppendOutput(output, data.Data, "stdout", "Information");
        process.ErrorDataReceived += (_, data) =>
            AppendOutput(output, data.Data, "stderr", "Error");

        try
        {
            if (!process.Start())
            {
                return
                [
                    ResourceDefinitionDiagnostic.Error(
                        "application.goApp.processStartFailed",
                        $"Go app process for '{resource.EffectiveResourceId}' did not start.",
                        resource.EffectiveResourceId)
                ];
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.goApp.processStartFailed",
                    $"Go app process for '{resource.EffectiveResourceId}' did not start. {exception.Message}",
                    resource.EffectiveResourceId)
            ];
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _processes.AddOrUpdate(
            resource.EffectiveResourceId,
            process,
            (_, previous) =>
            {
                DisposeProcess(previous);
                return process;
            });

        return [];
    }

    public IReadOnlyList<GoAppRuntimeOutputEntry> ReadOutput(
        string resourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        return _output.TryGetValue(resourceId, out var output)
            ? output.Read(maxEntries, before)
            : [];
    }

    private async ValueTask StopAsync(
        Resource resource,
        CancellationToken cancellationToken)
    {
        if (!_processes.TryRemove(resource.EffectiveResourceId, out var process))
        {
            return;
        }

        await StopProcessAsync(process, cancellationToken);
    }

    private async ValueTask<IReadOnlyDictionary<string, string>> ResolveRuntimeEnvironmentVariablesAsync(
        Resource resource,
        CancellationToken cancellationToken)
    {
        if (_environmentProviders.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in _environmentProviders)
        {
            var resolved = await provider.ResolveAsync(resource, cancellationToken);
            foreach (var (name, value) in resolved)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    variables[name.Trim()] = value;
                }
            }
        }

        return variables;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var resourceId in _processes.Keys.ToArray())
        {
            if (_processes.TryRemove(resourceId, out var process))
            {
                await StopProcessAsync(process, CancellationToken.None);
            }
        }
    }

    public void Dispose()
    {
        foreach (var resourceId in _processes.Keys.ToArray())
        {
            if (_processes.TryRemove(resourceId, out var process))
            {
                StopProcess(process);
            }
        }
    }

    private static async ValueTask StopProcessAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                process.Kill(entireProcessTree: true);
            }
            else
            {
                process.CloseMainWindow();
                await Task.WhenAny(
                    process.WaitForExitAsync(cancellationToken),
                    Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));

                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
        }
        finally
        {
            DisposeProcess(process);
        }
    }

    private static void StopProcess(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit(TimeSpan.FromSeconds(5));
        }
        finally
        {
            DisposeProcess(process);
        }
    }

    private static void DisposeProcess(Process process) =>
        process.Dispose();

    private static DateTimeOffset? TryGetStartTime(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static void AppendOutput(
        BoundedRuntimeOutputBuffer output,
        string? message,
        string stream,
        string severity)
    {
        if (!string.IsNullOrEmpty(message))
        {
            output.Append(new GoAppRuntimeOutputEntry(
                DateTimeOffset.UtcNow,
                message,
                stream,
                severity));
        }
    }

    private sealed class BoundedRuntimeOutputBuffer(int capacity = 1_000)
    {
        private readonly Lock _lock = new();
        private readonly Queue<GoAppRuntimeOutputEntry> _entries = new();

        public void Append(GoAppRuntimeOutputEntry entry)
        {
            lock (_lock)
            {
                _entries.Enqueue(entry);
                while (_entries.Count > capacity)
                {
                    _entries.Dequeue();
                }
            }
        }

        public IReadOnlyList<GoAppRuntimeOutputEntry> Read(
            int maxEntries,
            DateTimeOffset? before)
        {
            if (maxEntries <= 0)
            {
                return [];
            }

            lock (_lock)
            {
                return _entries
                    .Where(entry => before is null || entry.Timestamp < before)
                    .TakeLast(maxEntries)
                    .ToArray();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        }
    }
}

public sealed class NoopGoAppRuntimeController :
    IGoAppRuntimeController,
    IGoAppRuntimeOutputReader,
    IGoAppRuntimeMonitor
{
    public GoAppRuntimeStatus GetStatus(Resource resource) =>
        GoAppRuntimeStatus.Unknown;

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            GoAppRuntimeReadiness.CreateMissingDiagnostic(resource, operationId)
        ]);

    public IReadOnlyList<GoAppRuntimeOutputEntry> ReadOutput(
        string resourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null) =>
        [];

    public ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ResourceProcessMonitoringSnapshot?>(null);
}

internal static class GoAppRuntimeReadiness
{
    public const string DiagnosticCode = "application.goApp.runtimeControllerMissing";

    public static bool IsMissing(IGoAppRuntimeController? runtimeController) =>
        runtimeController is null or NoopGoAppRuntimeController;

    public static string CreateMissingReason(Resource resource, ResourceOperationId operationId) =>
        $"Go app resource '{resource.EffectiveResourceId}' cannot execute '{operationId}' because no Go app runtime controller is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(
        Resource resource,
        ResourceOperationId operationId) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource, operationId),
            resource.EffectiveResourceId);
}
