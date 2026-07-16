using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CloudShell.ControlPlane.Providers;

public interface IJavaScriptAppRuntimeController
{
    JavaScriptAppRuntimeStatus GetStatus(Resource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);
}

public interface IJavaScriptAppRuntimeOutputReader
{
    IReadOnlyList<JavaScriptAppRuntimeOutputEntry> ReadOutput(
        string resourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null);
}

public interface IJavaScriptAppRuntimeMonitor
{
    ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        string resourceId,
        CancellationToken cancellationToken = default);
}

public interface IJavaScriptAppRuntimeEnvironmentProvider
{
    ValueTask<IReadOnlyDictionary<string, string>> ResolveAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed record JavaScriptAppRuntimeOutputEntry(
    DateTimeOffset Timestamp,
    string Message,
    string Stream,
    string? Severity = null);

public enum JavaScriptAppRuntimeStatus
{
    Unknown,
    Stopped,
    Running
}

public sealed class JavaScriptAppProcessRuntimeController(
    IEnumerable<IJavaScriptAppRuntimeEnvironmentProvider>? environmentProviders = null) :
    IJavaScriptAppRuntimeController,
    IJavaScriptAppRuntimeOutputReader,
    IJavaScriptAppRuntimeMonitor,
    IDisposable,
    IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Process> _processes = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BoundedRuntimeOutputBuffer> _output = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly JavaScriptAppProcessCommandFactory _commands = new();
    private readonly IReadOnlyList<IJavaScriptAppRuntimeEnvironmentProvider> _environmentProviders =
        (environmentProviders ?? []).ToArray();

    public JavaScriptAppRuntimeStatus GetStatus(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return _processes.TryGetValue(resource.EffectiveResourceId, out var process) &&
            !process.HasExited
                ? JavaScriptAppRuntimeStatus.Running
                : JavaScriptAppRuntimeStatus.Stopped;
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
        if (operationId == JavaScriptAppResourceTypeProvider.Operations.Start)
        {
            return await StartAsync(resource, cancellationToken);
        }

        if (operationId == JavaScriptAppResourceTypeProvider.Operations.Stop)
        {
            await StopAsync(resource, cancellationToken);
            return [];
        }

        if (operationId == JavaScriptAppResourceTypeProvider.Operations.Restart)
        {
            await StopAsync(resource, cancellationToken);
            return await StartAsync(resource, cancellationToken);
        }

        return
        [
            ResourceDefinitionDiagnostic.Error(
                "application.javascriptApp.operationUnsupported",
                $"JavaScript app runtime does not support operation '{operationId}'.",
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
            JavaScriptAppResourceTypeProvider.Attributes.ProjectPath);
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.javascriptApp.pathRequired",
                    "JavaScript app project path is required.",
                    JavaScriptAppResourceTypeProvider.Attributes.ProjectPath)
            ];
        }

        var fullProjectPath = Path.GetFullPath(projectPath, Directory.GetCurrentDirectory());
        if (!Directory.Exists(fullProjectPath))
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.javascriptApp.projectDirectoryMissing",
                    $"JavaScript app project directory '{fullProjectPath}' does not exist.",
                    resource.EffectiveResourceId)
            ];
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
                        "application.javascriptApp.processStartFailed",
                        $"JavaScript app process for '{resource.EffectiveResourceId}' did not start.",
                        resource.EffectiveResourceId)
                ];
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.javascriptApp.processStartFailed",
                    $"JavaScript app process for '{resource.EffectiveResourceId}' did not start. {exception.Message}",
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

    public IReadOnlyList<JavaScriptAppRuntimeOutputEntry> ReadOutput(
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
            output.Append(new JavaScriptAppRuntimeOutputEntry(
                DateTimeOffset.UtcNow,
                message,
                stream,
                severity));
        }
    }

    private sealed class BoundedRuntimeOutputBuffer(int capacity = 1_000)
    {
        private readonly Lock _lock = new();
        private readonly Queue<JavaScriptAppRuntimeOutputEntry> _entries = new();

        public void Append(JavaScriptAppRuntimeOutputEntry entry)
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

        public IReadOnlyList<JavaScriptAppRuntimeOutputEntry> Read(
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

public sealed class NoopJavaScriptAppRuntimeController :
    IJavaScriptAppRuntimeController,
    IJavaScriptAppRuntimeOutputReader,
    IJavaScriptAppRuntimeMonitor
{
    public JavaScriptAppRuntimeStatus GetStatus(Resource resource) =>
        JavaScriptAppRuntimeStatus.Unknown;

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            JavaScriptAppRuntimeReadiness.CreateMissingDiagnostic(resource, operationId)
        ]);

    public IReadOnlyList<JavaScriptAppRuntimeOutputEntry> ReadOutput(
        string resourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null) =>
        [];

    public ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ResourceProcessMonitoringSnapshot?>(null);
}

internal static class JavaScriptAppRuntimeReadiness
{
    public const string DiagnosticCode = "application.javascriptApp.runtimeControllerMissing";

    public static bool IsMissing(IJavaScriptAppRuntimeController? runtimeController) =>
        runtimeController is null or NoopJavaScriptAppRuntimeController;

    public static string CreateMissingReason(Resource resource, ResourceOperationId operationId) =>
        $"JavaScript app resource '{resource.EffectiveResourceId}' cannot execute '{operationId}' because no JavaScript app runtime controller is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(
        Resource resource,
        ResourceOperationId operationId) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource, operationId),
            resource.EffectiveResourceId);
}
