using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    public static IServiceCollection AddInMemoryResourceModelGraphRecords(
        this IServiceCollection services,
        IEnumerable<ResourceRecord>? records = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var initialRecords = (records ?? []).ToArray();

        services.AddSingleton<IResourceStateProvider>(
            _ => new InMemoryResourceRecordStateProvider(initialRecords));
        services.AddSingleton<ResourceGraphModel>();

        return services;
    }

    public static IServiceCollection AddInMemoryResourceModelGraphRecords<TRecord>(
        this IServiceCollection services,
        IResourceGraphStoreProjector<TRecord> projector,
        IEnumerable<TRecord>? records = null)
        where TRecord : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(projector);

        var initialRecords = (records ?? []).ToArray();

        services.AddSingleton<IResourceStateProvider>(
            _ => new InMemoryProjectedResourceStateProvider<TRecord>(
                projector,
                initialRecords));
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
        services.AddResourceModelGraphInfrastructure();

        return services;
    }

    public static IServiceCollection AddResourceModelGraphServices(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddResourceModelResolver();
        services.AddResourceModelGraphInfrastructure();

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

        services.AddResourceModelResolver();

        return services;
    }

    public static IServiceCollection AddResourceModelResolver(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ResourceResolver>(serviceProvider =>
        {
            var typeProviders = serviceProvider
                .GetServices<IResourceTypeProvider>()
                .ToArray();

            return new ResourceResolver(
                serviceProvider
                    .GetServices<ResourceClassDefinition>()
                    .GroupBy(classDefinition => classDefinition.ClassId)
                    .Select(group => group.Last()),
                typeProviders.Select(provider => provider.TypeDefinition),
                serviceProvider.GetServices<IResourceAttributeValidator>());
        });

        return services;
    }

    private static IServiceCollection AddResourceModelGraphInfrastructure(
        this IServiceCollection services)
    {
        services.TryAddScoped<ResourceGraphResolver>();
        services.TryAddScoped<ResourceModelGraphResourceResolver>();
        services.TryAddScoped<ResourceProviderDispatcher>();
        services.TryAddScoped<ResourceCapabilityResolver>();
        services.TryAddScoped<ResourceOperationResolver>();
        services.TryAddScoped<ResourceProjectionResolver>();
        services.TryAddScoped<ResourceDefinitionValidationPipeline>();
        services.TryAddScoped<ResourceDefinitionGraphValidationPipeline>();
        services.TryAddScoped<ResourceDefinitionGraphProjectionResolver>();
        services.TryAddScoped<ResourceDefinitionGraphApplyPlanner>();
        services.TryAddScoped<ResourceChangeApplyDispatcher>();
        services.TryAddScoped<ResourceDefinitionGraphChangeApplier>();
        services.TryAddScoped<ResourceModelGraphDefinitionApplyService>();

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

    public static IServiceCollection AddResourceModelGraphProcedureProvider(
        this IServiceCollection services,
        string id,
        string displayName,
        ResourceDefinitionResolutionContext? resolutionContext = null,
        ResourceModelResourceManagerProjectionOptions? projectionOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddResourceModelGraphProcedureProvider(
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

    public static IServiceCollection AddResourceModelGraphProcedureProvider(
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

        services.AddScoped(serviceProvider =>
            new ResourceModelGraphProcedureProvider(
                new ResourceModelGraphResourceProvider(
                    id,
                    displayName,
                    () => resolveSnapshot(serviceProvider),
                    serviceProvider.GetRequiredService<ResourceResolver>(),
                    resolutionContext,
                    projectionOptions),
                serviceProvider.GetRequiredService<ResourceModelGraphResourceResolver>(),
                resolutionContext));
        services.AddScoped<IResourceProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ResourceModelGraphProcedureProvider>());
        services.AddScoped<IResourceActionAvailabilityProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ResourceModelGraphProcedureProvider>());

        return services;
    }
}
