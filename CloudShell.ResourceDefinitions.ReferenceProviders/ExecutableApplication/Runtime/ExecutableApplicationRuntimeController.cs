using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public interface IExecutableApplicationRuntimeController
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StartAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class ExecutableApplicationProcessRuntimeController :
    IExecutableApplicationRuntimeController,
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

        var executablePath = resource.Attributes.GetString(
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath);
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
            StartInfo = CreateStartInfo(resource, executablePath),
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

    internal static ProcessStartInfo CreateStartInfo(
        Resource resource,
        string executablePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath.Trim(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
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
}

public sealed class NoopExecutableApplicationRuntimeController :
    IExecutableApplicationRuntimeController
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StartAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}

public static class ExecutableApplicationEnvironmentNames
{
    public const string ResourceId = "CLOUDSHELL_RESOURCE_ID";
    public const string ResourceName = "CLOUDSHELL_RESOURCE_NAME";
}
