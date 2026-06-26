namespace CloudShell.ResourceDefinitions;

public interface IResourceDefinitionBuilder
{
    ResourceDefinition Build();
}

public sealed class ResourceDefinitionGraphBuilder
{
    private readonly List<IResourceDefinitionBuilder> _resources = [];

    public IReadOnlyList<IResourceDefinitionBuilder> Resources => _resources;

    public ResourceDefinitionGraphBuilder Add(IResourceDefinitionBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        _resources.Add(resource);
        return this;
    }

    public ResourceDefinitionGraphBuilder Add(ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return Add(new FixedResourceDefinitionBuilder(definition));
    }

    public ResourceDefinitionGraph BuildGraph() =>
        new(_resources.Select(resource => resource.Build()).ToArray());

    public ResourceDeploymentDefinition BuildDeployment(
        string name,
        string? environmentId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new ResourceDeploymentDefinition(
            name.Trim(),
            BuildGraph().Resources,
            environmentId,
            metadata);
    }

    private sealed class FixedResourceDefinitionBuilder(
        ResourceDefinition definition) : IResourceDefinitionBuilder
    {
        public ResourceDefinition Build() => definition;
    }
}
