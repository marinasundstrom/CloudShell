namespace CloudShell.ResourceDefinitions;

public interface IResourceDefinitionBuilder
{
    string EffectiveResourceId { get; }

    ResourceDefinition Build();
}

public abstract class ResourceDefinitionBuilder<TBuilder>(
    string name) : IResourceDefinitionBuilder
    where TBuilder : ResourceDefinitionBuilder<TBuilder>
{
    private readonly Dictionary<ResourceAttributeId, ResourceAttributeValue> _attributes = [];
    private readonly List<ResourceReference> _dependencies = [];
    private string? _resourceId;
    private string? _displayName;

    public string Name { get; } = NormalizeName(name);

    protected abstract ResourceTypeId TypeId { get; }

    protected abstract string? ProviderId { get; }

    protected IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeValue> Attributes =>
        _attributes;

    protected IReadOnlyList<ResourceReference> Dependencies => _dependencies;

    public string EffectiveResourceId =>
        string.IsNullOrWhiteSpace(_resourceId) ? $"{TypeId}:{Name}" : _resourceId;

    public TBuilder WithResourceId(string? resourceId)
    {
        _resourceId = string.IsNullOrWhiteSpace(resourceId) ? null : resourceId.Trim();
        return Self;
    }

    public TBuilder WithDisplayName(string? displayName)
    {
        _displayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        return Self;
    }

    public TBuilder DependsOn(string resourceId)
    {
        _dependencies.Add(ResourceReference.DependsOnResourceId(resourceId));
        return Self;
    }

    public TBuilder DependsOn(IResourceDefinitionBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return DependsOn(resource.EffectiveResourceId);
    }

    public TBuilder DependsOn(ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return DependsOn(definition.EffectiveResourceId);
    }

    public ResourceDefinition Build() =>
        new(
            Name,
            TypeId,
            ResourceId: _resourceId,
            ProviderId: ProviderId,
            DisplayName: _displayName,
            DependsOn: _dependencies.Count == 0
                ? null
                : _dependencies.ToArray(),
            Attributes: _attributes.Count == 0
                ? null
                : new ResourceAttributeValueMap(_attributes));

    protected TBuilder SetScalarAttribute(
        ResourceAttributeId attributeId,
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        _attributes[attributeId] = ResourceAttributeValue.String(value.Trim());
        return Self;
    }

    protected TBuilder SetScalarAttribute(
        ResourceAttributeId attributeId,
        bool value)
    {
        _attributes[attributeId] = ResourceAttributeValue.Boolean(value);
        return Self;
    }

    protected TBuilder SetScalarAttribute(
        ResourceAttributeId attributeId,
        long value)
    {
        _attributes[attributeId] = ResourceAttributeValue.Integer(value);
        return Self;
    }

    protected TBuilder AddDependency(ResourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        _dependencies.Add(reference);
        return Self;
    }

    private TBuilder Self => (TBuilder)this;

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }
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
        public string EffectiveResourceId => definition.EffectiveResourceId;

        public ResourceDefinition Build() => definition;
    }
}
