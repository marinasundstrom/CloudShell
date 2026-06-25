using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public enum ResourceWebAppRuntimeStatus
{
    Unknown,
    Stopped,
    Running
}

internal sealed record ResourceWebAppProcessOptions(
    string ServiceProjectPath,
    string DefinitionsEnvironmentVariable,
    string ResourceIdEnvironmentVariable,
    string DefinitionsFileName,
    TimeSpan StartupTimeout)
{
    public string? ServiceWorkingDirectory { get; init; }

    public string DefinitionsDirectory { get; init; } = Path.Combine(
        Path.GetTempPath(),
        "CloudShell.ResourceDefinitions",
        "Runtime");
}

internal sealed class ResourceWebAppProcessRuntime :
    IDisposable,
    IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, Process> _processes = new(
        StringComparer.OrdinalIgnoreCase);

    public ResourceWebAppRuntimeStatus GetStatus(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return _processes.TryGetValue(resource.EffectiveResourceId, out var process) &&
            !process.HasExited
                ? ResourceWebAppRuntimeStatus.Running
                : ResourceWebAppRuntimeStatus.Stopped;
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        ResourceAttributeId endpointAttributeId,
        ResourceWebAppProcessOptions options,
        Func<Resource, string?, object> createDefinition,
        string diagnosticPrefix,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        if (operationId == "start")
        {
            return await StartAsync(
                resource,
                endpointAttributeId,
                options,
                createDefinition,
                diagnosticPrefix,
                displayName,
                cancellationToken);
        }

        if (operationId == "stop")
        {
            await StopAsync(resource, cancellationToken);
            return [];
        }

        if (operationId == "restart")
        {
            await StopAsync(resource, cancellationToken);
            return await StartAsync(
                resource,
                endpointAttributeId,
                options,
                createDefinition,
                diagnosticPrefix,
                displayName,
                cancellationToken);
        }

        return
        [
            ResourceDefinitionDiagnostic.Error(
                $"{diagnosticPrefix}.operationUnsupported",
                $"{displayName} runtime does not support operation '{operationId}'.",
                resource.EffectiveResourceId)
        ];
    }

    private async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StartAsync(
        Resource resource,
        ResourceAttributeId endpointAttributeId,
        ResourceWebAppProcessOptions options,
        Func<Resource, string?, object> createDefinition,
        string diagnosticPrefix,
        string displayName,
        CancellationToken cancellationToken)
    {
        if (_processes.TryGetValue(resource.EffectiveResourceId, out var current) &&
            !current.HasExited)
        {
            return [];
        }

        var endpoint = resource.Attributes.GetString(endpointAttributeId);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    $"{diagnosticPrefix}.endpointRequired",
                    $"{displayName} endpoint is required before the backing service can start.",
                    endpointAttributeId)
            ];
        }

        var workingDirectory = string.IsNullOrWhiteSpace(options.ServiceWorkingDirectory)
            ? Directory.GetCurrentDirectory()
            : options.ServiceWorkingDirectory;
        var projectPath = Path.IsPathRooted(options.ServiceProjectPath)
            ? options.ServiceProjectPath
            : Path.GetFullPath(options.ServiceProjectPath, workingDirectory);
        if (!File.Exists(projectPath))
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    $"{diagnosticPrefix}.serviceProjectMissing",
                    $"{displayName} service project file '{projectPath}' does not exist.",
                    resource.EffectiveResourceId)
            ];
        }

        var definitionsPath = WriteDefinition(
            resource,
            endpoint,
            options,
            createDefinition);
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(endpoint);
        startInfo.Environment[options.DefinitionsEnvironmentVariable] = definitionsPath;
        startInfo.Environment[options.ResourceIdEnvironmentVariable] = resource.EffectiveResourceId;

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    $"{diagnosticPrefix}.serviceStartFailed",
                    $"{displayName} backing service process for '{resource.EffectiveResourceId}' did not start.",
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

        return await WaitForReadyAsync(
            resource,
            endpoint,
            options.StartupTimeout,
            diagnosticPrefix,
            displayName,
            cancellationToken);
    }

    private static string WriteDefinition(
        Resource resource,
        string? endpoint,
        ResourceWebAppProcessOptions options,
        Func<Resource, string?, object> createDefinition)
    {
        var directory = Path.Combine(options.DefinitionsDirectory, SanitizeFileName(resource.EffectiveResourceId));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, options.DefinitionsFileName);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, new[] { createDefinition(resource, endpoint) }, SerializerOptions);
        return path;
    }

    private static async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> WaitForReadyAsync(
        Resource resource,
        string endpoint,
        TimeSpan startupTimeout,
        string diagnosticPrefix,
        string displayName,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };
        var healthUrl = $"{endpoint.TrimEnd('/')}/healthz";
        var deadline = DateTimeOffset.UtcNow.Add(startupTimeout);
        Exception? lastException = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var response = await client.GetAsync(healthUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return [];
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                lastException = exception;
            }

            await Task.Delay(250, cancellationToken);
        }

        return
        [
            ResourceDefinitionDiagnostic.Error(
                $"{diagnosticPrefix}.serviceNotReady",
                $"{displayName} endpoint '{healthUrl}' did not become ready. {lastException?.Message}".Trim(),
                resource.EffectiveResourceId)
        ];
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
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            DisposeProcess(process);
        }
    }

    private static void DisposeProcess(Process process)
    {
        try
        {
            process.Dispose();
        }
        catch
        {
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character =>
            invalid.Contains(character) || character is ':' or '/' or '\\'
                ? '_'
                : character));
    }
}
