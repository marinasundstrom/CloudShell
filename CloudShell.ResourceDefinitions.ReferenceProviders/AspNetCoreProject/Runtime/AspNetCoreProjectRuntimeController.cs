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

public interface IAspNetCoreProjectRuntimeOutputReader
{
    IReadOnlyList<AspNetCoreProjectRuntimeOutputEntry> ReadOutput(
        string resourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null);
}

public interface IAspNetCoreProjectRuntimeEnvironmentProvider
{
    ValueTask<IReadOnlyDictionary<string, string>> ResolveAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed record AspNetCoreProjectRuntimeOutputEntry(
    DateTimeOffset Timestamp,
    string Message,
    string Stream,
    string? Severity = null);

public enum AspNetCoreProjectRuntimeStatus
{
    Unknown,
    Stopped,
    Running
}

public sealed class AspNetCoreProjectProcessRuntimeController :
    IAspNetCoreProjectRuntimeController,
    IAspNetCoreProjectRuntimeOutputReader,
    IDisposable,
    IAsyncDisposable
{
    private static readonly SemaphoreSlim BuildLock = new(1, 1);

    private readonly ConcurrentDictionary<string, Process> _processes = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BoundedRuntimeOutputBuffer> _output = new(
        StringComparer.OrdinalIgnoreCase);
    private readonly AspNetCoreProjectProcessCommandFactory _commands = new();
    private readonly IReadOnlyList<IAspNetCoreProjectRuntimeEnvironmentProvider> _environmentProviders;

    public AspNetCoreProjectProcessRuntimeController(
        ResourceGraphModel? graphModel = null,
        IEnumerable<IAspNetCoreProjectRuntimeEnvironmentProvider>? environmentProviders = null)
    {
        var providers = (environmentProviders ?? []).ToList();
        if (graphModel is not null &&
            !providers.OfType<AspNetCoreProjectServiceDiscoveryEnvironmentResolver>().Any())
        {
            providers.Insert(
                0,
                new AspNetCoreProjectServiceDiscoveryEnvironmentResolver(graphModel));
        }

        _environmentProviders = providers;
    }

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

        if (operationId == AspNetCoreProjectResourceTypeProvider.Operations.Stop)
        {
            await StopAsync(resource, cancellationToken);
            return [];
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
            AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath);
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.aspNetCoreProject.pathRequired",
                    "ASP.NET Core project path is required.",
                    AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath)
            ];
        }

        var fullProjectPath = Path.GetFullPath(projectPath, Directory.GetCurrentDirectory());
        if (!File.Exists(fullProjectPath))
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.aspNetCoreProject.projectFileMissing",
                    $"ASP.NET Core project file '{fullProjectPath}' does not exist.",
                    resource.EffectiveResourceId)
            ];
        }

        var output = _output.GetOrAdd(
            resource.EffectiveResourceId,
            _ => new BoundedRuntimeOutputBuffer());
        output.Clear();

        if (!GetBoolean(
                resource,
                AspNetCoreProjectResourceTypeProvider.Attributes.HotReload,
                defaultValue: true))
        {
            var buildDiagnostics = await BuildProjectAsync(
                resource,
                fullProjectPath,
                output,
                cancellationToken);
            if (buildDiagnostics.Count > 0)
            {
                return buildDiagnostics;
            }
        }

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

        if (!process.Start())
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "application.aspNetCoreProject.processStartFailed",
                    $"ASP.NET Core project process for '{resource.EffectiveResourceId}' did not start.",
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

    private static async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> BuildProjectAsync(
        Resource resource,
        string fullProjectPath,
        BoundedRuntimeOutputBuffer output,
        CancellationToken cancellationToken)
    {
        await BuildLock.WaitAsync(cancellationToken);
        try
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.GetDirectoryName(fullProjectPath) ?? Directory.GetCurrentDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("build");
            startInfo.ArgumentList.Add(fullProjectPath);
            startInfo.ArgumentList.Add("--nologo");
            startInfo.ArgumentList.Add("--disable-build-servers");

            using var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            process.OutputDataReceived += (_, data) =>
                AppendOutput(output, data.Data, "build", "Information");
            process.ErrorDataReceived += (_, data) =>
                AppendOutput(output, data.Data, "build", "Error");

            if (!process.Start())
            {
                return
                [
                    ResourceDefinitionDiagnostic.Error(
                        "application.aspNetCoreProject.buildStartFailed",
                        $"ASP.NET Core project build for '{resource.EffectiveResourceId}' did not start.",
                        resource.EffectiveResourceId)
                ];
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                return
                [
                    ResourceDefinitionDiagnostic.Error(
                        "application.aspNetCoreProject.buildFailed",
                        $"ASP.NET Core project '{fullProjectPath}' failed to build before starting '{resource.Name}'.",
                        resource.EffectiveResourceId)
                ];
            }

            return [];
        }
        finally
        {
            BuildLock.Release();
        }
    }

    public IReadOnlyList<AspNetCoreProjectRuntimeOutputEntry> ReadOutput(
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
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                variables[name.Trim()] = value;
            }
        }

        return variables;
    }

    private static bool GetBoolean(
        Resource resource,
        ResourceAttributeId attributeId,
        bool defaultValue) =>
        bool.TryParse(resource.Attributes.GetString(attributeId), out var value)
            ? value
            : defaultValue;

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

    private static void AppendOutput(
        BoundedRuntimeOutputBuffer output,
        string? message,
        string stream,
        string severity)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        output.Append(new AspNetCoreProjectRuntimeOutputEntry(
            DateTimeOffset.UtcNow,
            message,
            stream,
            severity));
    }

    private sealed class BoundedRuntimeOutputBuffer(int capacity = 1_000)
    {
        private readonly Lock _lock = new();
        private readonly Queue<AspNetCoreProjectRuntimeOutputEntry> _entries = new();

        public void Append(AspNetCoreProjectRuntimeOutputEntry entry)
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

        public IReadOnlyList<AspNetCoreProjectRuntimeOutputEntry> Read(
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

public sealed class NoopAspNetCoreProjectRuntimeController :
    IAspNetCoreProjectRuntimeController,
    IAspNetCoreProjectRuntimeOutputReader
{
    public AspNetCoreProjectRuntimeStatus GetStatus(Resource resource) =>
        AspNetCoreProjectRuntimeStatus.Unknown;

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);

    public IReadOnlyList<AspNetCoreProjectRuntimeOutputEntry> ReadOutput(
        string resourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null) =>
        [];
}

public static class AspNetCoreProjectEnvironmentNames
{
    public const string ResourceId = "CLOUDSHELL_RESOURCE_ID";
    public const string ResourceName = "CLOUDSHELL_RESOURCE_NAME";
    public const string DotNetWatchRestartOnRudeEdit = "DOTNET_WATCH_RESTART_ON_RUDE_EDIT";
}
