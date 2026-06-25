using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public interface IAspNetCoreProjectRuntimeController
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);
}

public sealed class AspNetCoreProjectProcessRuntimeController :
    IAspNetCoreProjectRuntimeController
{
    private readonly ConcurrentDictionary<string, Process> _processes = new(
        StringComparer.OrdinalIgnoreCase);

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

        var startInfo = CreateStartInfo(
            resource,
            fullProjectPath,
            useHotReload: GetBoolean(
                resource,
                AspNetCoreProjectResourceTypeProvider.Attributes.HotReload,
                defaultValue: true));
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

    private static ProcessStartInfo CreateStartInfo(
        Resource resource,
        string fullProjectPath,
        bool useHotReload)
    {
        var arguments = new StringBuilder();
        if (useHotReload)
        {
            arguments.Append("watch --project ");
            arguments.Append(Quote(fullProjectPath));
            arguments.Append(" run");
        }
        else
        {
            arguments.Append("run --project ");
            arguments.Append(Quote(fullProjectPath));
        }

        var projectArguments = resource.Attributes.GetString(
            AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments);
        if (!string.IsNullOrWhiteSpace(projectArguments))
        {
            arguments.Append(" -- ");
            arguments.Append(projectArguments);
        }

        var startInfo = new ProcessStartInfo("dotnet", arguments.ToString())
        {
            WorkingDirectory = Path.GetDirectoryName(fullProjectPath) ?? Directory.GetCurrentDirectory(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.Environment[AspNetCoreProjectEnvironmentNames.ResourceId] =
            resource.EffectiveResourceId;
        startInfo.Environment[AspNetCoreProjectEnvironmentNames.ResourceName] =
            resource.Name;

        return startInfo;
    }

    private async ValueTask StopAsync(
        Resource resource,
        CancellationToken cancellationToken)
    {
        if (!_processes.TryRemove(resource.EffectiveResourceId, out var process))
        {
            return;
        }

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

    private static bool GetBoolean(
        Resource resource,
        ResourceAttributeId attributeId,
        bool defaultValue) =>
        bool.TryParse(resource.Attributes.GetString(attributeId), out var value)
            ? value
            : defaultValue;

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static void DisposeProcess(Process process)
    {
        process.Dispose();
    }
}

public sealed class NoopAspNetCoreProjectRuntimeController :
    IAspNetCoreProjectRuntimeController
{
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
