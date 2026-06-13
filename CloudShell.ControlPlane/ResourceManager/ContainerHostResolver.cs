using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class ContainerHostResolver(
    IResourceManagerStore resourceManager,
    IResourceRegistrationStore registrations,
    IEnumerable<IResourceOrchestrationDescriptorProvider> descriptorProviders,
    IEnumerable<IContainerHostProvider> containerHostProviders,
    IEnumerable<IContainerEngineProvider> containerEngineProviders) : IContainerHostResolver
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IReadOnlyList<IResourceOrchestrationDescriptorProvider> descriptorProviders =
        descriptorProviders.ToArray();
    private readonly IReadOnlyList<IContainerHostProvider> containerHostProviders =
        containerHostProviders.ToArray();
    private readonly IReadOnlyList<IContainerEngineProvider> containerEngineProviders =
        containerEngineProviders.ToArray();

    public async Task<ContainerHostResolutionResult> ResolveAsync(
        ContainerHostResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetResourceId);

        var explicitHostId = FirstNonEmpty(request.ExplicitHostResourceId);
        if (explicitHostId is not null)
        {
            return await ResolveByIdAsync(
                explicitHostId,
                $"Container host '{explicitHostId}' is not registered.",
                cancellationToken);
        }

        var preferredHostId = FirstNonEmpty(request.PreferredHostId);
        if (preferredHostId is not null)
        {
            return await ResolveByIdAsync(
                preferredHostId,
                $"Preferred container host '{preferredHostId}' is not registered.",
                cancellationToken);
        }

        var configuredDefault = GetConfiguredHosts()
            .Where(host => host.IsDefault)
            .OrderBy(host => host.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (configuredDefault is not null)
        {
            return new ContainerHostResolutionResult(configuredDefault);
        }

        var resourceDefault = await ResolveDefaultResourceHostAsync(cancellationToken);
        return resourceDefault is not null
            ? new ContainerHostResolutionResult(resourceDefault)
            : new ContainerHostResolutionResult(
                null,
                $"Resource '{request.TargetResourceId}' is container-backed but no default container host is registered. Use UseDocker(), UseContainerEngine(...), or set an explicit container host.");
    }

    private async Task<ContainerHostResolutionResult> ResolveByIdAsync(
        string hostId,
        string notFoundMessage,
        CancellationToken cancellationToken)
    {
        var configuredHost = GetConfiguredHosts()
            .FirstOrDefault(host => string.Equals(host.Id, hostId, StringComparison.OrdinalIgnoreCase));
        if (configuredHost is not null)
        {
            return new ContainerHostResolutionResult(configuredHost);
        }

        var resource = resourceManager.GetResource(hostId);
        if (resource is null)
        {
            return new ContainerHostResolutionResult(null, notFoundMessage);
        }

        var descriptor = await TryDescribeAsync(resource, cancellationToken);
        var host = descriptor is null ? null : TryReadContainerHost(descriptor);
        return host is not null
            ? new ContainerHostResolutionResult(host)
            : new ContainerHostResolutionResult(null, notFoundMessage);
    }

    private async Task<ContainerHostDescriptor?> ResolveDefaultResourceHostAsync(
        CancellationToken cancellationToken)
    {
        foreach (var resource in resourceManager.GetResources().OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = await TryDescribeAsync(resource, cancellationToken);
            if (descriptor is null)
            {
                continue;
            }

            var host = TryReadContainerHost(descriptor);
            if (host?.IsDefault == true)
            {
                return host;
            }
        }

        return null;
    }

    private IReadOnlyList<ContainerHostDescriptor> GetConfiguredHosts() =>
        containerEngineProviders
            .Select(provider => provider.GetContainerEngine().ToContainerHostDescriptor())
            .Concat(containerHostProviders.Select(provider => provider.GetDefaultHost()))
            .Where(host => !string.IsNullOrWhiteSpace(host.Id))
            .GroupBy(host => host.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();

    private async Task<ResourceOrchestrationDescriptor?> TryDescribeAsync(
        Resource resource,
        CancellationToken cancellationToken)
    {
        var provider = descriptorProviders.FirstOrDefault(provider => provider.CanDescribe(resource));
        if (provider is null)
        {
            return null;
        }

        try
        {
            return await provider.DescribeAsync(
                resource,
                new ResourceOrchestrationDescriptorContext(
                    registrations.GetRegistration(resource.Id),
                    resourceManager.GetGroupForResource(resource.Id),
                    resourceManager),
                cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static ContainerHostDescriptor? TryReadContainerHost(
        ResourceOrchestrationDescriptor descriptor)
    {
        try
        {
            if (descriptor.ResourceType.Equals(
                    ContainerHostResourceTypes.ContainerHost,
                    StringComparison.OrdinalIgnoreCase))
            {
                return descriptor.Configuration.Deserialize<ContainerHostDescriptor>(SerializerOptions);
            }

            if (descriptor.ResourceType.Equals(
                    ContainerEngineResourceTypes.ContainerEngine,
                    StringComparison.OrdinalIgnoreCase))
            {
                return descriptor.Configuration
                    .Deserialize<ContainerEngineResourceDefinition>(SerializerOptions)?
                    .ToContainerHostDescriptor();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
