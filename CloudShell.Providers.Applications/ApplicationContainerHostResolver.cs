using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace CloudShell.Providers.Applications;

public sealed class ApplicationContainerHostResolver(IServiceProvider serviceProvider)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<ContainerHostDescriptor?> ResolveAsync(
        string? containerHostId,
        string? preferredContainerHostId,
        IResourceManagerStore resourceManager,
        string? requiredCapability,
        CancellationToken cancellationToken)
    {
        var selectedEngineId = FirstNonEmpty(containerHostId, preferredContainerHostId);
        if (!string.IsNullOrWhiteSpace(selectedEngineId))
        {
            var selectedHost = await ResolveByIdAsync(selectedEngineId, resourceManager, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Container host '{selectedEngineId}' is not registered.");
            ValidateContainerHost(selectedHost, requiredCapability);
            return selectedHost;
        }

        var defaultHost = GetContainerHosts()
            .Where(engine => engine.IsDefault)
            .OrderBy(engine => engine.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? await ResolveDefaultResourceAsync(resourceManager, cancellationToken);
        if (defaultHost is not null)
        {
            ValidateContainerHost(defaultHost, requiredCapability);
        }

        return defaultHost;
    }

    public ContainerHostDescriptor? ResolveStatic(ApplicationResourceDefinition definition)
    {
        var hosts = GetContainerHosts();
        if (!string.IsNullOrWhiteSpace(definition.ContainerHostId))
        {
            return hosts.FirstOrDefault(host =>
                string.Equals(host.Id, definition.ContainerHostId, StringComparison.OrdinalIgnoreCase));
        }

        return hosts
            .Where(host => host.IsDefault)
            .OrderBy(host => host.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public async Task<ContainerHostDescriptor?> ResolveStaticAsync(
        ApplicationResourceDefinition definition,
        CancellationToken cancellationToken)
    {
        var staticHost = ResolveStatic(definition);
        if (staticHost is not null)
        {
            return staticHost;
        }

        using var scope = serviceProvider.CreateScope();
        var resourceManager = scope.ServiceProvider.GetService<IResourceManagerStore>();
        if (resourceManager is null)
        {
            return null;
        }

        var selectedEngineId = FirstNonEmpty(definition.ContainerHostId);
        if (!string.IsNullOrWhiteSpace(selectedEngineId))
        {
            return await ResolveByIdAsync(selectedEngineId, resourceManager, cancellationToken);
        }

        return await ResolveDefaultResourceAsync(resourceManager, cancellationToken);
    }

    public IReadOnlyList<ContainerHostDescriptor> GetContainerHosts() =>
        serviceProvider
            .GetServices<IContainerHostProvider>()
            .Select(provider => provider.GetDefaultHost())
            .Where(engine => !string.IsNullOrWhiteSpace(engine.Id))
            .GroupBy(engine => engine.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();

    private async Task<ContainerHostDescriptor?> ResolveByIdAsync(
        string engineId,
        IResourceManagerStore resourceManager,
        CancellationToken cancellationToken)
    {
        var engine = GetContainerHosts()
            .FirstOrDefault(engine => string.Equals(engine.Id, engineId, StringComparison.OrdinalIgnoreCase));
        if (engine is not null)
        {
            return engine;
        }

        var resource = resourceManager.GetResource(engineId);
        if (resource is null)
        {
            return null;
        }

        if (resource.State is not ResourceState.Running)
        {
            throw new InvalidOperationException(
                $"Container host '{engineId}' is unavailable.");
        }

        var descriptor = await TryDescribeContainerHostAsync(resource, resourceManager, cancellationToken);
        return descriptor is null ? null : TryReadContainerHost(descriptor);
    }

    private async Task<ContainerHostDescriptor?> ResolveDefaultResourceAsync(
        IResourceManagerStore resourceManager,
        CancellationToken cancellationToken)
    {
        foreach (var resource in resourceManager.GetResources())
        {
            var descriptor = await TryDescribeContainerHostAsync(resource, resourceManager, cancellationToken);
            if (descriptor is null)
            {
                continue;
            }

            var engine = TryReadContainerHost(descriptor);
            if (engine?.IsDefault == true)
            {
                if (resource.State is not ResourceState.Running)
                {
                    throw new InvalidOperationException(
                        $"Container host '{engine.Id}' is unavailable.");
                }

                return engine;
            }
        }

        return null;
    }

    private async Task<ResourceOrchestrationDescriptor?> TryDescribeContainerHostAsync(
        Resource resource,
        IResourceManagerStore resourceManager,
        CancellationToken cancellationToken)
    {
        var provider = serviceProvider
            .GetServices<IResourceOrchestrationDescriptorProvider>()
            .FirstOrDefault(provider => provider.CanDescribe(resource));
        if (provider is null)
        {
            return null;
        }

        return await provider.DescribeAsync(
            resource,
            new ResourceOrchestrationDescriptorContext(
                null,
                resourceManager.GetGroupForResource(resource.Id),
                resourceManager),
            cancellationToken);
    }

    private static ContainerHostDescriptor? TryReadContainerHost(ResourceOrchestrationDescriptor descriptor)
    {
        if (!descriptor.ResourceType.Equals(ContainerHostResourceTypes.ContainerHost, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return descriptor.Configuration.Deserialize<ContainerHostDescriptor>(SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ValidateContainerHost(
        ContainerHostDescriptor containerHost,
        string? requiredCapability)
    {
        if (!containerHost.CredentialsAvailable)
        {
            throw new InvalidOperationException(
                $"Container host '{containerHost.Id}' credentials are unavailable.");
        }

        if (!string.IsNullOrWhiteSpace(requiredCapability) &&
            !containerHost.HostCapabilities.Contains(requiredCapability, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Container host '{containerHost.Id}' does not advertise required capability '{requiredCapability}'.");
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
