using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;
using ResourceModelResource = CloudShell.ResourceDefinitions.Resource;

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
                serviceProvider.GetServices<IResourceAttributeValidator>(),
                serviceProvider.GetServices<IResourceAttributeValueShapeProvider>());
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
            CreateResourceModelGraphResourceProvider(
                serviceProvider,
                id,
                displayName,
                resolveSnapshot,
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
                CreateResourceModelGraphResourceProvider(
                    serviceProvider,
                    id,
                    displayName,
                    resolveSnapshot,
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

    private static ResourceModelGraphResourceProvider CreateResourceModelGraphResourceProvider(
        IServiceProvider serviceProvider,
        string id,
        string displayName,
        Func<IServiceProvider, ResourceGraphSnapshot> resolveSnapshot,
        ResourceDefinitionResolutionContext? resolutionContext,
        ResourceModelResourceManagerProjectionOptions? projectionOptions) =>
        new(
            id,
            displayName,
            () => resolveSnapshot(serviceProvider),
            serviceProvider.GetRequiredService<ResourceResolver>(),
            serviceProvider.GetServices<IResourceGraphDependencyProvider>(),
            resolutionContext,
            ComposeProjectionOptions(serviceProvider, projectionOptions));

    private static ResourceModelResourceManagerProjectionOptions ComposeProjectionOptions(
        IServiceProvider serviceProvider,
        ResourceModelResourceManagerProjectionOptions? projectionOptions)
    {
        var options = projectionOptions ?? new ResourceModelResourceManagerProjectionOptions();
        var stateProviders = serviceProvider
            .GetServices<IResourceModelResourceManagerStateProvider>()
            .ToArray();
        var endpointProjectionProviders = serviceProvider
            .GetServices<IResourceModelResourceManagerEndpointProjectionProvider>()
            .ToArray();

        if (stateProviders.Length == 0 && endpointProjectionProviders.Length == 0)
        {
            return options;
        }

        var stateResolver = options.StateResolver;
        var endpointProjectionResolver = options.EndpointProjectionResolver;
        return options with
        {
            StateResolver = stateProviders.Length == 0
                ? stateResolver
                : resource => ResolveState(resource, stateResolver, stateProviders),
            EndpointProjectionResolver = endpointProjectionProviders.Length == 0
                ? endpointProjectionResolver
                : resource => ResolveEndpointProjection(
                    resource,
                    endpointProjectionResolver,
                    endpointProjectionProviders)
        };
    }

    private static ResourceManagerState? ResolveState(
        ResourceModelResource resource,
        ResourceModelResourceManagerStateResolver? stateResolver,
        IReadOnlyList<IResourceModelResourceManagerStateProvider> stateProviders)
    {
        var state = stateResolver?.Invoke(resource);
        if (state is not null)
        {
            return state;
        }

        foreach (var stateProvider in stateProviders)
        {
            state = stateProvider.GetState(resource);
            if (state is not null)
            {
                return state;
            }
        }

        return null;
    }

    private static ResourceModelResourceManagerEndpointProjection? ResolveEndpointProjection(
        ResourceModelResource resource,
        ResourceModelResourceManagerEndpointProjectionResolver? endpointProjectionResolver,
        IReadOnlyList<IResourceModelResourceManagerEndpointProjectionProvider> endpointProjectionProviders)
    {
        var projection = endpointProjectionResolver?.Invoke(resource);
        if (projection is not null)
        {
            return projection;
        }

        foreach (var projectionProvider in endpointProjectionProviders)
        {
            projection = projectionProvider.GetEndpointProjection(resource);
            if (projection is not null)
            {
                return projection;
            }
        }

        return null;
    }
}
