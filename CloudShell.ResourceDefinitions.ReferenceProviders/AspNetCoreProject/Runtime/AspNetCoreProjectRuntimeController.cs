using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public interface IAspNetCoreProjectRuntimeController
{
    AspNetCoreProjectRuntimeStatus GetStatus(Resource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);
}

public enum AspNetCoreProjectRuntimeStatus
{
    Unknown,
    Stopped,
    Running
}

public sealed class AspNetCoreProjectProcessRuntimeController :
    IAspNetCoreProjectRuntimeController,
    IDisposable,
    IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Process> _processes = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly AspNetCoreProjectProcessCommandFactory _commands = new();

    public AspNetCoreProjectRuntimeStatus GetStatus(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return _processes.TryGetValue(resource.EffectiveResourceId, out var process) &&
            !process.HasExited
                ? AspNetCoreProjectRuntimeStatus.Running
                : AspNetCoreProjectRuntimeStatus.Stopped;
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (operationId == AspNetCoreProjectResourceTypeProvider.Operations.Start)
        {
            return await StartAsync(resource, cancellationToken);
        }

        if (operationId == AspNetCoreProjectResourceTypeProvider.Operations.Restart)
        {
            await StopAsync(resource, cancellationToken);
            return await StartAsync(resource, cancellationToken);
        }

        return
        [
            ResourceDefinitionDiagnostic.Error(
                "application.aspNetCoreProject.operationUnsupported",
                $"ASP.NET Core project runtime does not support operation '{operationId}'.",
                resource.EffectiveResourceId)
        ];
    }

    private ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StartAsync(
        Resource resource,
        CancellationToken cancellationToken)
    {
        if (_processes.TryGetValue(resource.EffectiveResourceId, out var current) &&
            !current.HasExited)
        {
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        var projectPath = resource.Attributes.GetString(
            AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath);
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.aspNetCoreProject.pathRequired",
                    "ASP.NET Core project path is required.",
                    AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath)
            ]);
        }

        var fullProjectPath = Path.GetFullPath(projectPath, Directory.GetCurrentDirectory());
        if (!File.Exists(fullProjectPath))
        {
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.aspNetCoreProject.projectFileMissing",
                    $"ASP.NET Core project file '{fullProjectPath}' does not exist.",
                    resource.EffectiveResourceId)
            ]);
        }

        var startInfo = _commands.CreateStartInfo(resource, fullProjectPath);
        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };

        if (!process.Start())
        {
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.aspNetCoreProject.processStartFailed",
                    $"ASP.NET Core project process for '{resource.EffectiveResourceId}' did not start.",
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

public sealed class NoopAspNetCoreProjectRuntimeController :
    IAspNetCoreProjectRuntimeController
{
    public AspNetCoreProjectRuntimeStatus GetStatus(Resource resource) =>
        AspNetCoreProjectRuntimeStatus.Unknown;

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}

public static class AspNetCoreProjectEnvironmentNames
{
    public const string ResourceId = "CLOUDSHELL_RESOURCE_ID";
    public const string ResourceName = "CLOUDSHELL_RESOURCE_NAME";
}
