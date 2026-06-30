using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CloudShell.ControlPlane.Providers;

public interface IExecutableApplicationRuntimeController
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StartAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public interface IExecutableApplicationRuntimeMonitor
{
    ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        string resourceId,
        CancellationToken cancellationToken = default);
}

public sealed class ExecutableApplicationProcessRuntimeController :
    IExecutableApplicationRuntimeController,
    IExecutableApplicationRuntimeMonitor,
    IDisposable,
    IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Process> _processes = new(
        StringComparer.OrdinalIgnoreCase);

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StartAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (_processes.TryGetValue(resource.EffectiveResourceId, out var current) &&
            !current.HasExited)
        {
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        var configuration = resource.GetConfiguration<ExecutableApplicationConfiguration>(
            ExecutableApplicationResourceTypeProvider.ConfigurationSection);
        var executablePath = FirstNonEmpty(
            resource.Attributes.GetString(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath),
            configuration?.Path);
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.executable.pathRequired",
                    "Executable path is required.",
                    ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath)
            ]);
        }

        var process = new Process
        {
            StartInfo = CreateStartInfo(resource, executablePath, configuration),
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };

        try
        {
            if (!process.Start())
            {
                DisposeProcess(process);
                return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
                [
                    ResourceDefinitionDiagnostic.Error(
                        "application.executable.processStartFailed",
                        $"Executable application process for '{resource.EffectiveResourceId}' did not start.",
                        resource.EffectiveResourceId)
                ]);
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

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            DisposeProcess(process);
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.executable.processStartFailed",
                    $"Executable application process for '{resource.EffectiveResourceId}' could not start: {exception.Message}",
                    resource.EffectiveResourceId)
            ]);
        }
    }

    public async ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return null;
        }

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

    internal static ProcessStartInfo CreateStartInfo(
        Resource resource,
        string executablePath,
        ExecutableApplicationConfiguration? configuration = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath.Trim(),
            Arguments = configuration?.Arguments?.Trim() ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (!string.IsNullOrWhiteSpace(configuration?.WorkingDirectory))
        {
            startInfo.WorkingDirectory = configuration.WorkingDirectory.Trim();
        }

        startInfo.Environment[ExecutableApplicationEnvironmentNames.ResourceId] =
            resource.EffectiveResourceId;
        startInfo.Environment[ExecutableApplicationEnvironmentNames.ResourceName] =
            resource.Name;

        return startInfo;
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

    private static void DisposeProcess(Process process)
    {
        process.Dispose();
    }

    private static DateTimeOffset? TryGetStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}

public sealed class NoopExecutableApplicationRuntimeController :
    IExecutableApplicationRuntimeController,
    IExecutableApplicationRuntimeMonitor
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StartAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);

    public ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ResourceProcessMonitoringSnapshot?>(null);
}

public static class ExecutableApplicationEnvironmentNames
{
    public const string ResourceId = "CLOUDSHELL_RESOURCE_ID";
    public const string ResourceName = "CLOUDSHELL_RESOURCE_NAME";
}
