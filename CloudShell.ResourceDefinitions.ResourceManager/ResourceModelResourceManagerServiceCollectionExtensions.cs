using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public static class ResourceModelResourceManagerServiceCollectionExtensions
{
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
