using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class LocalExecutableResourceOrchestrationDescriptorOptions
{
    private readonly Dictionary<string, LocalExecutableResourceOrchestrationDescriptorDefinition> resources =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, LocalExecutableResourceOrchestrationDescriptorDefinition> Resources => resources;

    public LocalExecutableResourceOrchestrationDescriptorOptions AddResource(
        string resourceId,
        string configurationVersion,
        Action<LocalExecutableResourceOrchestrationDescriptorDefinition>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationVersion);

        var definition = new LocalExecutableResourceOrchestrationDescriptorDefinition(
            resourceId,
            configurationVersion);
        configure?.Invoke(definition);
        resources[resourceId] = definition;
        return this;
    }
}

public sealed class LocalExecutableResourceOrchestrationDescriptorDefinition(
    string resourceId,
    string configurationVersion)
{
    public string ResourceId { get; } = resourceId;

    public string ConfigurationVersion { get; set; } = configurationVersion;
}

public sealed class LocalExecutableResourceOrchestrationDescriptorProvider(
    IOptions<LocalExecutableResourceOrchestrationDescriptorOptions> options) :
    IResourceOrchestrationDescriptorProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalExecutableResourceOrchestrationDescriptorOptions options = options.Value;

    public bool CanDescribe(ResourceManagerResource resource) =>
        options.Resources.ContainsKey(resource.Id);

    public Task<ResourceOrchestrationDescriptor> DescribeAsync(
        ResourceManagerResource resource,
        ResourceOrchestrationDescriptorContext context,
        CancellationToken cancellationToken = default)
    {
        var definition = options.Resources[resource.Id];
        var workload = new ResourceWorkloadConfiguration(
            ResourceWorkloadKind.LocalExecutable,
            resource.Name,
            Lifetime: ResourceLifetime.ControlPlaneScoped);

        return Task.FromResult(new ResourceOrchestrationDescriptor(
            resource.Id,
            resource.EffectiveTypeId,
            resource.DependsOn,
            [],
            resource.Endpoints,
            definition.ConfigurationVersion,
            JsonSerializer.SerializeToElement(workload, SerializerOptions)));
    }
}

public static class LocalExecutableResourceOrchestrationDescriptorServiceCollectionExtensions
{
    public static IServiceCollection AddLocalExecutableResourceOrchestrationDescriptors(
        this IServiceCollection services,
        Action<LocalExecutableResourceOrchestrationDescriptorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOrchestrationDescriptorProvider, LocalExecutableResourceOrchestrationDescriptorProvider>());

        return services;
    }
}
