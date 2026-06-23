using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager.Orchestration;

public sealed class ContainerHostResolver(
    IResourceManagerStore resourceManager,
    IResourceRegistrationStore registrations,
    IEnumerable<IResourceOrchestrationDescriptorProvider> descriptorProviders,
    IEnumerable<IContainerHostProvider> containerHostProviders) : IContainerHostResolver
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IReadOnlyList<IResourceOrchestrationDescriptorProvider> descriptorProviders =
        descriptorProviders.ToArray();
    private readonly IReadOnlyList<IContainerHostProvider> containerHostProviders =
        containerHostProviders.ToArray();

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
                request.RequiredCapability,
                cancellationToken);
        }

        var preferredHostId = FirstNonEmpty(request.PreferredHostId);
        if (preferredHostId is not null)
        {
            return await ResolveByIdAsync(
                preferredHostId,
                $"Preferred container host '{preferredHostId}' is not registered.",
                request.RequiredCapability,
                cancellationToken);
        }

        var configuredDefault = GetConfiguredHosts()
            .Where(host => host.IsDefault)
            .OrderBy(host => host.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (configuredDefault is not null)
        {
            return ValidateHost(configuredDefault, request.RequiredCapability);
        }

        var resourceDefault = await ResolveDefaultResourceHostAsync(request.RequiredCapability, cancellationToken);
        return resourceDefault is not null
            ? resourceDefault
            : new ContainerHostResolutionResult(
                null,
                $"Resource '{ResourceDisplayLabels.GetName(request.TargetResourceId)}' is container-backed but no default container host is registered. Use UseDocker(), UseContainerHost(...), or set an explicit container host.",
                ContainerHostResolutionFailureReason.DefaultHostMissing);
    }

    private async Task<ContainerHostResolutionResult> ResolveByIdAsync(
        string hostId,
        string notFoundMessage,
        string? requiredCapability,
        CancellationToken cancellationToken)
    {
        var configuredHost = GetConfiguredHosts()
            .FirstOrDefault(host => string.Equals(host.Id, hostId, StringComparison.OrdinalIgnoreCase));
        if (configuredHost is not null)
        {
            return ValidateHost(configuredHost, requiredCapability);
        }

        var resource = resourceManager.GetResource(hostId);
        if (resource is null)
        {
            return new ContainerHostResolutionResult(
                null,
                notFoundMessage,
                ContainerHostResolutionFailureReason.HostNotRegistered);
        }

        var descriptor = await TryDescribeAsync(resource, cancellationToken);
        var host = descriptor is null ? null : TryReadContainerHost(descriptor);
        return host is not null
            ? ValidateHost(host, requiredCapability, resource)
            : new ContainerHostResolutionResult(
                null,
                notFoundMessage,
                ContainerHostResolutionFailureReason.HostNotRegistered);
    }

    private async Task<ContainerHostResolutionResult?> ResolveDefaultResourceHostAsync(
        string? requiredCapability,
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
                return ValidateHost(host, requiredCapability, resource);
            }
        }

        return null;
    }

    private static ContainerHostResolutionResult ValidateHost(
        ContainerHostDescriptor host,
        string? requiredCapability,
        Resource? hostResource = null)
    {
        if (hostResource is not null && hostResource.State is not ResourceState.Running)
        {
            return new ContainerHostResolutionResult(
                null,
                $"Container host '{host.Id}' is unavailable.",
                ContainerHostResolutionFailureReason.HostUnavailable);
        }

        if (!host.CredentialsAvailable)
        {
            return new ContainerHostResolutionResult(
                null,
                $"Container host '{host.Id}' credentials are unavailable.",
                ContainerHostResolutionFailureReason.CredentialsUnavailable);
        }

        requiredCapability = FirstNonEmpty(requiredCapability);
        if (requiredCapability is not null &&
            !host.HostCapabilities.Contains(requiredCapability, StringComparer.OrdinalIgnoreCase))
        {
            return new ContainerHostResolutionResult(
                null,
                $"Container host '{host.Id}' does not advertise required capability '{requiredCapability}'.",
                ContainerHostResolutionFailureReason.RequiredCapabilityMissing);
        }

        return new ContainerHostResolutionResult(host);
    }

    private IReadOnlyList<ContainerHostDescriptor> GetConfiguredHosts() =>
        containerHostProviders
            .Select(provider => provider.GetDefaultHost())
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
