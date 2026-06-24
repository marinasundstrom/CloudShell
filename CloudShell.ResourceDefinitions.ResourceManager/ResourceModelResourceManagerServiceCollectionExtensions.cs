using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public static class ResourceModelResourceManagerServiceCollectionExtensions
{
    public static IServiceCollection AddInMemoryResourceModelGraph(
        this IServiceCollection services,
        IEnumerable<ResourceState>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var initialResources = (resources ?? []).ToArray();

        services.AddSingleton<IResourceStateProvider>(
            _ => new InMemoryResourceStateProvider(initialResources));
        services.AddSingleton<ResourceGraphModel>();

        return services;
    }

    public static IServiceCollection AddResourceModelGraphServices(
        this IServiceCollection services,
        IEnumerable<ResourceClassDefinition> classDefinitions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(classDefinitions);

        services.AddResourceModelResolver(classDefinitions);
        services.AddScoped<ResourceGraphResolver>();
        services.AddScoped<ResourceModelGraphResourceResolver>();
        services.AddScoped<ResourceProviderDispatcher>();
        services.AddScoped<ResourceCapabilityResolver>();
        services.AddScoped<ResourceOperationResolver>();
        services.AddScoped<ResourceProjectionResolver>();
        services.AddScoped<ResourceDefinitionValidationPipeline>();
        services.AddScoped<ResourceDefinitionGraphValidationPipeline>();
        services.AddScoped<ResourceDefinitionGraphProjectionResolver>();
        services.AddScoped<ResourceDefinitionGraphApplyPlanner>();
        services.AddScoped<ResourceChangeApplyDispatcher>();

        return services;
    }

    public static IServiceCollection AddResourceModelResolver(
        this IServiceCollection services,
        IEnumerable<ResourceClassDefinition> classDefinitions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(classDefinitions);

        foreach (var classDefinition in classDefinitions)
        {
            services.AddSingleton(classDefinition);
        }

        services.AddScoped(serviceProvider =>
        {
            var typeProviders = serviceProvider
                .GetServices<IResourceTypeProvider>()
                .ToArray();

            return new ResourceResolver(
                serviceProvider.GetServices<ResourceClassDefinition>(),
                typeProviders.Select(provider => provider.TypeDefinition),
                serviceProvider.GetServices<IResourceAttributeValidator>());
        });

        return services;
    }

    public static IServiceCollection AddResourceModelGraphResourceProvider(
        this IServiceCollection services,
        string id,
        string displayName,
        ResourceDefinitionResolutionContext? resolutionContext = null,
        ResourceModelResourceManagerProjectionOptions? projectionOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddResourceModelGraphResourceProvider(
            id,
            displayName,
            serviceProvider => serviceProvider
                .GetRequiredService<ResourceGraphModel>()
                .GetSnapshotAsync()
                .AsTask()
                .GetAwaiter()
                .GetResult(),
            resolutionContext,
            projectionOptions);
    }

    public static IServiceCollection AddResourceModelGraphResourceProvider(
        this IServiceCollection services,
        string id,
        string displayName,
        Func<IServiceProvider, ResourceGraphSnapshot> resolveSnapshot,
        ResourceDefinitionResolutionContext? resolutionContext = null,
        ResourceModelResourceManagerProjectionOptions? projectionOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(resolveSnapshot);

        services.AddScoped<IResourceProvider>(serviceProvider =>
            new ResourceModelGraphResourceProvider(
                id,
                displayName,
                () => resolveSnapshot(serviceProvider),
                serviceProvider.GetRequiredService<ResourceResolver>(),
                resolutionContext,
                projectionOptions));

        return services;
    }
}
