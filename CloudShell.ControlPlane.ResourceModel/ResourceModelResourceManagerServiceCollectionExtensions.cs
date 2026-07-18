using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;
using ResourceModelResource = CloudShell.ResourceModel.Resource;
using ResourceState = CloudShell.ResourceModel.ResourceState;

namespace CloudShell.ControlPlane.ResourceModel;

public static class ResourceModelResourceManagerServiceCollectionExtensions
{
    public static IServiceCollection AddInMemoryResourceModelGraph(
        this IServiceCollection services,
        IEnumerable<ResourceState>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var initialResources = GetOrAddInitialStateStore(services);
        initialResources.Upsert(resources ?? []);

        services.TryAddSingleton<IResourceStateProvider>(
            serviceProvider => new InMemoryResourceStateProvider(
                serviceProvider.GetRequiredService<ResourceModelInitialStateStore>().GetResources()));
        services.TryAddSingleton<ResourceGraphModel>();

        return services;
    }

    public static IServiceCollection AddDefaultInMemoryResourceModelGraphResources(
        this IServiceCollection services,
        IEnumerable<ResourceState>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var initialResources = GetOrAddInitialStateStore(services);
        initialResources.AddIfAbsent(resources ?? []);

        services.TryAddSingleton<IResourceStateProvider>(
            serviceProvider => new InMemoryResourceStateProvider(
                serviceProvider.GetRequiredService<ResourceModelInitialStateStore>().GetResources()));
        services.TryAddSingleton<ResourceGraphModel>();

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
                serviceProvider.GetServices<IResourceAttributeValueShapeProvider>(),
                serviceProvider.GetServices<IResourceCapabilityAttributeProvider>());
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
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                IResourceModelGraphApplyReconciler,
                ResourceModelGraphMaterializedChangeReconciler>());
        services.TryAddScoped<ResourceDefinitionTemplateService>();
        services.TryAddScoped<IResourceDefinitionRegistrationService, ResourceDefinitionRegistrationService>();

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
            ResolveAvailableResourceGraphSnapshot,
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
            ResolveAvailableResourceGraphSnapshot,
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
                serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>(),
                resolutionContext,
                serviceProvider.GetServices<IResourceModelGraphDeploymentDescriptor>(),
                serviceProvider.GetServices<IResourceModelGraphOrchestratorServiceExecutor>()));
        services.AddScoped<IResourceProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ResourceModelGraphProcedureProvider>());
        services.AddScoped<IResourceActionAvailabilityProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ResourceModelGraphProcedureProvider>());
        services.AddScoped<IResourceOrchestratorDeploymentProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ResourceModelGraphProcedureProvider>());
        services.AddScoped<IResourceOrchestratorServiceProcedureProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ResourceModelGraphProcedureProvider>());
        services.AddScoped<IResourceEnvironmentVariableConfigurationProvider>(
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

    private static ResourceGraphSnapshot ResolveAvailableResourceGraphSnapshot(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<ResourceGraphModel>().GetSnapshotIfAvailable()
        ?? new ResourceGraphSnapshot(ResourceGraphVersion.Initial, []);

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
        var observabilityProviders = serviceProvider
            .GetServices<IResourceModelResourceManagerObservabilityProvider>()
            .ToArray();
        var attributeProviders = serviceProvider
            .GetServices<IResourceModelResourceManagerAttributeProvider>()
            .ToArray();
        var parentProviders = serviceProvider
            .GetServices<IResourceModelResourceManagerParentProvider>()
            .ToArray();

        if (stateProviders.Length == 0 &&
            endpointProjectionProviders.Length == 0 &&
            observabilityProviders.Length == 0 &&
            attributeProviders.Length == 0 &&
            parentProviders.Length == 0)
        {
            return options;
        }

        var stateResolver = options.StateResolver;
        var endpointProjectionResolver = options.EndpointProjectionResolver;
        var observabilityResolver = options.ObservabilityResolver;
        var attributeResolver = options.AttributeResolver;
        var parentResourceIdResolver = options.ParentResourceIdResolver;
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
                    endpointProjectionProviders),
            ObservabilityResolver = observabilityProviders.Length == 0
                ? observabilityResolver
                : resource => ResolveObservability(
                    resource,
                    observabilityResolver,
                    observabilityProviders),
            AttributeResolver = attributeProviders.Length == 0
                ? attributeResolver
                : resource => ResolveAttributes(
                    resource,
                    attributeResolver,
                    attributeProviders),
            ParentResourceIdResolver = parentProviders.Length == 0
                ? parentResourceIdResolver
                : resource => ResolveParentResourceId(
                    resource,
                    parentResourceIdResolver,
                    parentProviders)
        };
    }

    private static ResourceModelInitialStateStore GetOrAddInitialStateStore(IServiceCollection services)
    {
        var store = services
            .Where(descriptor => descriptor.ServiceType == typeof(ResourceModelInitialStateStore))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ResourceModelInitialStateStore>()
            .SingleOrDefault();
        if (store is not null)
        {
            return store;
        }

        store = new ResourceModelInitialStateStore();
        services.AddSingleton(store);
        return store;
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

    private static ResourceObservability? ResolveObservability(
        ResourceModelResource resource,
        ResourceModelResourceManagerObservabilityResolver? observabilityResolver,
        IReadOnlyList<IResourceModelResourceManagerObservabilityProvider> observabilityProviders)
    {
        var observability = observabilityResolver?.Invoke(resource);
        if (observability is not null)
        {
            return observability;
        }

        foreach (var observabilityProvider in observabilityProviders)
        {
            observability = observabilityProvider.GetObservability(resource);
            if (observability is not null)
            {
                return observability;
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string>? ResolveAttributes(
        ResourceModelResource resource,
        ResourceModelResourceManagerAttributeResolver? attributeResolver,
        IReadOnlyList<IResourceModelResourceManagerAttributeProvider> attributeProviders)
    {
        var attributes = attributeResolver?.Invoke(resource);
        foreach (var attributeProvider in attributeProviders)
        {
            var providerAttributes = attributeProvider.GetAttributes(resource);
            if (providerAttributes is null || providerAttributes.Count == 0)
            {
                continue;
            }

            if (attributes is null)
            {
                attributes = providerAttributes;
                continue;
            }

            attributes = attributes
                .Concat(providerAttributes)
                .GroupBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Value,
                    StringComparer.OrdinalIgnoreCase);
        }

        return attributes;
    }

    private static string? ResolveParentResourceId(
        ResourceModelResource resource,
        ResourceModelResourceManagerParentResourceIdResolver? parentResourceIdResolver,
        IReadOnlyList<IResourceModelResourceManagerParentProvider> parentProviders)
    {
        var parentResourceId = parentResourceIdResolver?.Invoke(resource);
        if (!string.IsNullOrWhiteSpace(parentResourceId))
        {
            return parentResourceId;
        }

        foreach (var parentProvider in parentProviders)
        {
            parentResourceId = parentProvider.GetParentResourceId(resource);
            if (!string.IsNullOrWhiteSpace(parentResourceId))
            {
                return parentResourceId;
            }
        }

        return null;
    }
}

internal sealed class ResourceModelInitialStateStore
{
    private readonly Dictionary<string, ResourceState> _resources = new(StringComparer.OrdinalIgnoreCase);

    public void Upsert(IEnumerable<ResourceState> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        foreach (var resource in resources)
        {
            _resources[resource.EffectiveResourceId] = resource;
        }
    }

    public void AddIfAbsent(IEnumerable<ResourceState> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        foreach (var resource in resources)
        {
            _resources.TryAdd(resource.EffectiveResourceId, resource);
        }
    }

    public IReadOnlyList<ResourceState> GetResources() =>
        _resources.Values.ToArray();
}
