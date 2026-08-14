using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public enum BuiltInServiceRuntimeMode
{
    Process,
    Container
}

internal static class BuiltInServiceContainerRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddBuiltInServiceContainerRuntime(
        this IServiceCollection services)
    {
        services.AddContainerHostCommandPlatform();
        services.TryAddSingleton<
            ILocalContainerApplicationCommandRunner,
            ProcessLocalContainerApplicationCommandRunner>();
        services.TryAddSingleton<IContainerHostRuntime, CommandContainerHostRuntime>();
        return services;
    }
}

internal sealed record ResourceWebAppContainerOptions(
    string Image,
    string ContainerNamePrefix,
    string DefinitionsEnvironmentVariable,
    string ResourceIdEnvironmentVariable,
    string DefinitionsFileName,
    TimeSpan StartupTimeout)
{
    public string DefinitionsDirectory { get; init; } = Path.Combine(
        Path.GetTempPath(),
        "CloudShell.ResourceModel",
        "Runtime");

    public int ContainerHttpPort { get; init; } = 8080;

    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ContainerHostPortBinding> AdditionalPublishedPorts { get; init; } = [];
}

internal sealed class ResourceWebAppContainerRuntime(
    IContainerHostRuntime containerHostRuntime) : IAsyncDisposable
{
    private const string ContainerDefinitionsDirectory = "/cloudshell/definitions";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, ContainerHostRuntimeHandle> _containers = new(
        StringComparer.OrdinalIgnoreCase);

    public ResourceWebAppRuntimeStatus GetStatus(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return _containers.ContainsKey(resource.EffectiveResourceId)
            ? ResourceWebAppRuntimeStatus.Running
            : ResourceWebAppRuntimeStatus.Stopped;
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        ResourceAttributeId endpointAttributeId,
        ResourceWebAppContainerOptions options,
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
            return await StopAsync(resource, options, diagnosticPrefix, displayName, cancellationToken);
        }

        if (operationId == "restart")
        {
            var stopDiagnostics = await StopAsync(
                resource,
                options,
                diagnosticPrefix,
                displayName,
                cancellationToken);
            if (stopDiagnostics.Count > 0)
            {
                return stopDiagnostics;
            }

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
                $"{displayName} container runtime does not support operation '{operationId}'.",
                resource.EffectiveResourceId)
        ];
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (resourceId, handle) in _containers.ToArray())
        {
            if (_containers.TryRemove(resourceId, out _))
            {
                await containerHostRuntime.RemoveContainerAsync(handle, CancellationToken.None);
            }
        }
    }

    private async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StartAsync(
        Resource resource,
        ResourceAttributeId endpointAttributeId,
        ResourceWebAppContainerOptions options,
        Func<Resource, string?, object> createDefinition,
        string diagnosticPrefix,
        string displayName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Image))
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    $"{diagnosticPrefix}.containerImageRequired",
                    $"{displayName} container image is required in container runtime mode.",
                    resource.EffectiveResourceId)
            ];
        }

        var endpoint = resource.Attributes.GetString(endpointAttributeId);
        if (!TryCreateHttpPortBinding(endpoint, options.ContainerHttpPort, out var httpBinding))
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    $"{diagnosticPrefix}.containerEndpointUnsupported",
                    $"{displayName} endpoint '{endpoint}' must be an absolute HTTP endpoint with an explicit port before the backing container can start.",
                    endpointAttributeId)
            ];
        }

        var containerName = CreateContainerName(options.ContainerNamePrefix, resource.EffectiveResourceId);
        var handle = new ContainerHostRuntimeHandle(resource.EffectiveResourceId, containerName);
        var inspection = await containerHostRuntime.InspectContainerAsync(handle, cancellationToken);
        if (inspection.Container?.State == ContainerHostContainerState.Running)
        {
            _containers[resource.EffectiveResourceId] = handle;
            return [];
        }

        if (inspection.Found)
        {
            await containerHostRuntime.RemoveContainerAsync(handle, cancellationToken);
        }

        var definitionsPath = WriteDefinition(resource, endpoint, options, createDefinition);
        var containerDefinitionsPath = $"{ContainerDefinitionsDirectory}/{options.DefinitionsFileName}";
        var environmentVariables = new Dictionary<string, string>(
            options.EnvironmentVariables,
            StringComparer.OrdinalIgnoreCase)
        {
            ["ASPNETCORE_URLS"] = $"http://0.0.0.0:{options.ContainerHttpPort.ToString(CultureInfo.InvariantCulture)}",
            [options.DefinitionsEnvironmentVariable] = containerDefinitionsPath,
            [options.ResourceIdEnvironmentVariable] = resource.EffectiveResourceId
        };
        var definitionDirectory = Path.GetDirectoryName(definitionsPath)!;
        var result = await containerHostRuntime.RunContainerAsync(
            new ContainerHostContainerSpec(
                resource.EffectiveResourceId,
                containerName,
                options.Image,
                PublishedPorts: [httpBinding, .. options.AdditionalPublishedPorts],
                EnvironmentVariables: environmentVariables,
                Mounts: [new(definitionDirectory, ContainerDefinitionsDirectory, ReadOnly: true)],
                RemoveWhenStopped: false),
            cancellationToken);
        if (!result.Succeeded)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    $"{diagnosticPrefix}.containerStartFailed",
                    $"{displayName} container '{containerName}' did not start. {FirstNonEmpty(result.Error, result.Output)}".Trim(),
                    resource.EffectiveResourceId)
            ];
        }

        _containers[resource.EffectiveResourceId] = handle;
        var diagnostics = await WaitForReadyAsync(
            resource,
            endpoint!,
            options.StartupTimeout,
            diagnosticPrefix,
            displayName,
            cancellationToken);
        if (diagnostics.Count > 0)
        {
            _containers.TryRemove(resource.EffectiveResourceId, out _);
            await containerHostRuntime.RemoveContainerAsync(handle, CancellationToken.None);
        }

        return diagnostics;
    }

    private async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StopAsync(
        Resource resource,
        ResourceWebAppContainerOptions options,
        string diagnosticPrefix,
        string displayName,
        CancellationToken cancellationToken)
    {
        var handle = _containers.TryRemove(resource.EffectiveResourceId, out var tracked)
            ? tracked
            : new ContainerHostRuntimeHandle(
                resource.EffectiveResourceId,
                CreateContainerName(options.ContainerNamePrefix, resource.EffectiveResourceId));
        var result = await containerHostRuntime.RemoveContainerAsync(handle, cancellationToken);
        if (result.Succeeded || IsMissingContainer(result))
        {
            return [];
        }

        return
        [
            ResourceDefinitionDiagnostic.Error(
                $"{diagnosticPrefix}.containerStopFailed",
                $"{displayName} container '{handle.ContainerName}' could not be removed. {FirstNonEmpty(result.Error, result.Output)}".Trim(),
                resource.EffectiveResourceId)
        ];
    }

    private static string WriteDefinition(
        Resource resource,
        string? endpoint,
        ResourceWebAppContainerOptions options,
        Func<Resource, string?, object> createDefinition)
    {
        var directory = Path.Combine(options.DefinitionsDirectory, Sanitize(resource.EffectiveResourceId));
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
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
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

    private static bool TryCreateHttpPortBinding(
        string? endpoint,
        int containerPort,
        out ContainerHostPortBinding binding)
    {
        binding = null!;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme != "http" ||
            uri.IsDefaultPort)
        {
            return false;
        }

        var hostAddress = uri.Host is "localhost" or "127.0.0.1" or "::1"
            ? "127.0.0.1"
            : uri.Host;
        binding = new(hostAddress, uri.Port, containerPort);
        return true;
    }

    private static string CreateContainerName(string prefix, string resourceId) =>
        $"cloudshell-{Sanitize(prefix).ToLowerInvariant()}-{Sanitize(resourceId).ToLowerInvariant()}";

    private static string Sanitize(string value) =>
        string.Concat(value.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-'))
            .Trim('-');

    private static bool IsMissingContainer(ContainerHostOperationResult result) =>
        result.Error.Contains("No such container", StringComparison.OrdinalIgnoreCase) ||
        result.Output.Contains("No such container", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
