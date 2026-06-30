using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResourceState = CloudShell.ResourceModel.ResourceState;

namespace CloudShell.ControlPlane.ResourceModel;

public static class ResourceModelGraphBuilderHostExtensions
{
    public static IControlPlaneBuilder DefineResources(
        this IControlPlaneBuilder builder,
        Action<ControlPlaneResourceGraphBuilder> configure,
        Func<ResourceState, ResourceState>? projectState = null,
        IResourceIdConvention? resourceIdConvention = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var convention = resourceIdConvention ?? DefaultResourceIdConvention.Instance;
        builder.Services.TryAddSingleton<IResourceIdConvention>(convention);

        var declarations = ResourceModelResourceDefinitionBuilderExtensions.GetOrAddDeclarationStore(builder.Services);
        var graph = new ControlPlaneResourceGraphBuilder(builder, declarations, convention);
        graph.SeedIdentityProviderDeclarations(declarations);
        configure(graph);
        RegisterDeclarations(builder, graph);
        builder.Services.AddInMemoryResourceModelGraph(
            ProjectStates(
                graph.BuildGraph().Resources.Select(ResourceState.FromDefinition),
                projectState));

        return builder;
    }

    public static IControlPlaneBuilder DefineInitialTemplate(
        this IControlPlaneBuilder builder,
        string name,
        Action<ControlPlaneResourceGraphBuilder> configure,
        string? environmentId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        Func<ResourceState, ResourceState>? projectState = null,
        IResourceIdConvention? resourceIdConvention = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        var convention = resourceIdConvention ?? DefaultResourceIdConvention.Instance;
        builder.Services.TryAddSingleton<IResourceIdConvention>(convention);

        var declarations = ResourceModelResourceDefinitionBuilderExtensions.GetOrAddDeclarationStore(builder.Services);
        var graph = new ControlPlaneResourceGraphBuilder(builder, declarations, convention);
        graph.SeedIdentityProviderDeclarations(declarations);
        configure(graph);
        RegisterDeclarations(builder, graph);
        var template = graph.BuildTemplate(
            name,
            environmentId,
            metadata);
        builder.Services.AddInMemoryResourceModelGraph(
            ProjectStates(
                template.Resources.Select(ResourceState.FromDefinition),
                projectState));

        return builder;
    }

    private static void RegisterDeclarations(
        IControlPlaneBuilder builder,
        ControlPlaneResourceGraphBuilder graph)
    {
        var declarations = ResourceModelResourceDefinitionBuilderExtensions
            .GetOrAddDeclarationStore(builder.Services);
        foreach (var resource in graph.ResourceBuilders)
        {
            var metadata = ResourceModelResourceDefinitionBuilderExtensions.GetDeclarationMetadata(resource);
            var declaration = declarations.Declare(
                builder,
                ResourceModelResourceProvider.DefaultProviderId,
                resource.EffectiveResourceId,
                resourceGroupId: metadata.ResourceGroupId);
            if (metadata.Identity is { } identity)
            {
                declaration.WithIdentity(identity);
            }

            if (metadata.AutoStart is { } autoStart)
            {
                declaration.WithAutoStart(autoStart);
            }

            if (metadata.DependencyAutoStart is { } dependencyAutoStart)
            {
                declaration.WithDependencyAutoStart(dependencyAutoStart);
            }

            if (metadata.ProvisionIdentityOnStartup is { } provisionIdentityOnStartup)
            {
                declaration.ProvisionIdentityOnStartup(provisionIdentityOnStartup);
            }
        }

        foreach (var resource in graph.ResourceBuilders)
        {
            var metadata = ResourceModelResourceDefinitionBuilderExtensions.GetDeclarationMetadata(resource);
            foreach (var grant in metadata.PermissionGrants)
            {
                declarations.AddPermissionGrant(grant);
            }
        }
    }

    private static IEnumerable<ResourceState> ProjectStates(
        IEnumerable<ResourceState> states,
        Func<ResourceState, ResourceState>? projectState) =>
        projectState is null
            ? states
            : states.Select(projectState);
}
