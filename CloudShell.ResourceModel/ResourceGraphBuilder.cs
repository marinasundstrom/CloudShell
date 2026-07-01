using System.Text.Json;

namespace CloudShell.ResourceModel;

public interface IResourceDefinitionBuilder
{
    string Name { get; }

    ResourceTypeId ResourceTypeId { get; }

    string? ResourceProviderId { get; }

    string EffectiveResourceId { get; }

    ResourceDefinition Build();
}

internal interface IResourceIdConventionAwareBuilder
{
    void UseResourceIdConvention(IResourceIdConvention resourceIdConvention);
}

public interface IResourceGraphContextAwareBuilder
{
    void UseResourceGraph(ResourceGraphBuilder graph);
}

public abstract class ResourceDefinitionBuilder<TBuilder>(
    string name) :
    IResourceDefinitionBuilder,
    IResourceIdConventionAwareBuilder,
    IResourceGraphContextAwareBuilder
    where TBuilder : ResourceDefinitionBuilder<TBuilder>
{
    private readonly Dictionary<ResourceAttributeId, ResourceAttributeValue> _attributes = [];
    private readonly List<ResourceReference> _dependencies = [];
    private readonly Dictionary<string, JsonElement> _configuration = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ResourceCapabilityId, JsonElement> _capabilities = [];
    private readonly Dictionary<ResourceOperationId, JsonElement> _operations = [];
    private string? _resourceId;
    private string? _displayName;
    private ResourceGraphBuilder? _resourceGraph;
    private IResourceIdConvention _resourceIdConvention = DefaultResourceIdConvention.Instance;

    public string Name { get; } = NormalizeName(name);

    public ResourceTypeId ResourceTypeId => TypeId;

    public string? ResourceProviderId => ProviderId;

    protected abstract ResourceTypeId TypeId { get; }

    protected abstract string? ProviderId { get; }

    protected IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeValue> Attributes =>
        _attributes;

    protected IReadOnlyList<ResourceReference> Dependencies => _dependencies;

    protected IReadOnlyDictionary<string, JsonElement> Configuration => _configuration;

    protected IReadOnlyDictionary<ResourceCapabilityId, JsonElement> Capabilities => _capabilities;

    protected IReadOnlyDictionary<ResourceOperationId, JsonElement> Operations => _operations;

    protected ResourceGraphBuilder? ResourceGraph => _resourceGraph;

    public string EffectiveResourceId =>
        string.IsNullOrWhiteSpace(_resourceId)
            ? ResourceIdConventionResolver.Resolve(_resourceIdConvention, Name, TypeId, ProviderId)
            : _resourceId;

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

    public TBuilder WithConfiguration<TConfiguration>(
        string sectionName,
        TConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        ArgumentNullException.ThrowIfNull(configuration);

        _configuration[sectionName.Trim()] = ResourceDefinitionJson.FromValue(configuration);
        return Self;
    }

    public TBuilder DependsOn(string resourceId)
    {
        _dependencies.Add(ResourceReference.DependsOnResourceId(resourceId));
        return Self;
    }

    public TBuilder DependsOn(
        string resourceId,
        ResourceTypeId? typeId,
        string? providerId = null)
    {
        _dependencies.Add(ResourceReference.DependsOnResourceId(
            resourceId,
            typeId,
            providerId));
        return Self;
    }

    public TBuilder DependsOn(IResourceDefinitionBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return DependsOn(
            resource.EffectiveResourceId,
            resource.ResourceTypeId,
            resource.ResourceProviderId);
    }

    public TBuilder DependsOn(
        IResourceDefinitionBuilder resource,
        ResourceTypeId? typeId,
        string? providerId = null)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return DependsOn(
            resource.EffectiveResourceId,
            typeId ?? resource.ResourceTypeId,
            providerId ?? resource.ResourceProviderId);
    }

    public TBuilder DependsOn(ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return DependsOn(definition.EffectiveResourceId);
    }

    public ResourceDefinition Build()
    {
        OnBeforeBuild();

        return new(
            Name,
            TypeId,
            ResourceId: EffectiveResourceId,
            ProviderId: ProviderId,
            DisplayName: _displayName,
            DependsOn: _dependencies.Count == 0
                ? null
                : _dependencies.ToArray(),
            Attributes: _attributes.Count == 0
                ? null
                : new ResourceAttributeValueMap(_attributes),
            Configuration: _configuration.Count == 0
                ? null
                : new Dictionary<string, JsonElement>(_configuration, StringComparer.OrdinalIgnoreCase),
            Capabilities: _capabilities.Count == 0
                ? null
                : new Dictionary<ResourceCapabilityId, JsonElement>(_capabilities),
            Operations: _operations.Count == 0
                ? null
                : new Dictionary<ResourceOperationId, JsonElement>(_operations));
    }

    void IResourceIdConventionAwareBuilder.UseResourceIdConvention(
        IResourceIdConvention resourceIdConvention)
    {
        ArgumentNullException.ThrowIfNull(resourceIdConvention);

        _resourceIdConvention = resourceIdConvention;
    }

    void IResourceGraphContextAwareBuilder.UseResourceGraph(
        ResourceGraphBuilder graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        _resourceGraph = graph;
    }

    protected virtual void OnBeforeBuild()
    {
    }

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

    protected TBuilder SetObjectAttribute<TValue>(
        ResourceAttributeId attributeId,
        TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _attributes[attributeId] = ResourceAttributeValue.FromObject(value);
        return Self;
    }

    protected TBuilder AddDependency(ResourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        _dependencies.Add(reference);
        return Self;
    }

    protected TBuilder RemoveDependencies(Func<ResourceReference, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        _dependencies.RemoveAll(reference => predicate(reference));
        return Self;
    }

    protected TBuilder SetConfiguration<TConfiguration>(
        string sectionName,
        TConfiguration configuration)
        => WithConfiguration(sectionName, configuration);

    protected TBuilder SetCapability<TCapability>(
        ResourceCapabilityId capabilityId,
        TCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);

        _capabilities[capabilityId] = ResourceDefinitionJson.FromValue(capability);
        return Self;
    }

    protected TBuilder DeclareCapability(ResourceCapabilityId capabilityId)
    {
        _capabilities[capabilityId] = ResourceDefinitionJson.FromValue(new Dictionary<string, string>());
        return Self;
    }

    protected TBuilder SetOperation<TOperation>(
        ResourceOperationId operationId,
        TOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        _operations[operationId] = ResourceDefinitionJson.FromValue(operation);
        return Self;
    }

    private TBuilder Self => (TBuilder)this;

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }
}

public class ResourceGraphBuilder(
    IResourceIdConvention? resourceIdConvention = null)
{
    private readonly List<IResourceDefinitionBuilder> _resources = [];
    private readonly IResourceIdConvention _resourceIdConvention =
        resourceIdConvention ?? DefaultResourceIdConvention.Instance;

    public IReadOnlyList<IResourceDefinitionBuilder> ResourceBuilders => _resources;

    public ResourceGraphBuilder DefineResources(
        Action<ResourceGraphBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(this);
        return this;
    }

    public ResourceGraphBuilder Add(IResourceDefinitionBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (resource is IResourceIdConventionAwareBuilder conventionAware)
        {
            conventionAware.UseResourceIdConvention(_resourceIdConvention);
        }

        var resourceId = resource.EffectiveResourceId;
        if (_resources.Any(existing => string.Equals(
                existing.EffectiveResourceId,
                resourceId,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Resource '{resourceId}' is already defined in the graph.");
        }

        if (resource is IResourceGraphContextAwareBuilder contextAware)
        {
            contextAware.UseResourceGraph(this);
        }

        _resources.Add(resource);
        return this;
    }

    public ResourceGraphBuilder Add(ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return Add(new FixedResourceDefinitionBuilder(definition));
    }

    public TBuilder GetOrAddResource<TBuilder>(
        string resourceId,
        Func<TBuilder> createResource)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(createResource);

        var normalizedResourceId = resourceId.Trim();
        if (_resources.FirstOrDefault(resource => string.Equals(
                resource.EffectiveResourceId,
                normalizedResourceId,
                StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            return existing is TBuilder typed
                ? typed
                : throw new InvalidOperationException(
                    $"Resource '{normalizedResourceId}' is already defined as '{existing.ResourceTypeId}'.");
        }

        var created = createResource();
        Add(created);
        if (!string.Equals(created.EffectiveResourceId, normalizedResourceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Default resource builder created '{created.EffectiveResourceId}', expected '{normalizedResourceId}'.");
        }

        return created;
    }

    public ResourceDefinitionGraph BuildGraph()
    {
        var definitions = new List<ResourceDefinition>();
        for (var index = 0; index < _resources.Count; index++)
        {
            definitions.Add(_resources[index].Build());
        }

        return new ResourceDefinitionGraph(definitions);
    }

    public ResourceTemplate BuildTemplate(
        string name,
        string? environmentId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new ResourceTemplate(
            name.Trim(),
            BuildGraph().Resources,
            environmentId,
            metadata);
    }

    private sealed class FixedResourceDefinitionBuilder(
        ResourceDefinition definition) :
        IResourceDefinitionBuilder,
        IResourceIdConventionAwareBuilder
    {
        private IResourceIdConvention _resourceIdConvention = DefaultResourceIdConvention.Instance;

        public string Name => definition.Name;

        public ResourceTypeId ResourceTypeId => definition.TypeId;

        public string? ResourceProviderId => definition.ProviderId;

        public string EffectiveResourceId =>
            string.IsNullOrWhiteSpace(definition.ResourceId)
                ? ResourceIdConventionResolver.Resolve(
                    _resourceIdConvention,
                    definition.Name,
                    definition.TypeId,
                    definition.ProviderId)
                : definition.ResourceId;

        public ResourceDefinition Build() =>
            definition with
            {
                ResourceId = EffectiveResourceId
            };

        public void UseResourceIdConvention(IResourceIdConvention resourceIdConvention)
        {
            ArgumentNullException.ThrowIfNull(resourceIdConvention);

            _resourceIdConvention = resourceIdConvention;
        }
    }
}
