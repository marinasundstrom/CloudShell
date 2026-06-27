using CloudShell.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public static class ResourceModelGraphBuilderHostExtensions
{
    public static IControlPlaneBuilder DefineResources(
        this IControlPlaneBuilder builder,
        Action<ResourceDefinitionGraphBuilder> configure,
        Func<ResourceState, ResourceState>? projectState = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var graph = new ResourceDefinitionGraphBuilder();
        graph.DefineResources(configure);
        builder.Services.AddInMemoryResourceModelGraph(
            ProjectStates(
                graph.BuildGraph().Resources.Select(ResourceState.FromDefinition),
                projectState));

        return builder;
    }

    public static IControlPlaneBuilder DefineDeployment(
        this IControlPlaneBuilder builder,
        string name,
        Action<ResourceDefinitionGraphBuilder> configure,
        string? environmentId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        Func<ResourceState, ResourceState>? projectState = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        var graph = new ResourceDefinitionGraphBuilder();
        graph.DefineResources(configure);
        var deployment = graph.BuildDeployment(
            name,
            environmentId,
            metadata);
        builder.Services.AddInMemoryResourceModelGraph(
            ProjectStates(
                deployment.Resources.Select(ResourceState.FromDefinition),
                projectState));

        return builder;
    }

    private static IEnumerable<ResourceState> ProjectStates(
        IEnumerable<ResourceState> states,
        Func<ResourceState, ResourceState>? projectState) =>
        projectState is null
            ? states
            : states.Select(projectState);
}
