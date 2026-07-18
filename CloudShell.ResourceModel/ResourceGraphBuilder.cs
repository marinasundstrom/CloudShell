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

public interface IResourceDefinitionAttributeBuilder
{
    IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeValue> AttributeValues { get; }

    void SetAttribute(ResourceAttributeId attributeId, ResourceAttributeValue value);

    void RemoveAttribute(ResourceAttributeId attributeId);
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
    IResourceDefinitionAttributeBuilder,
    IResourceIdConventionAwareBuilder,
    IResourceGraphContextAwareBuilder
    where TBuilder : ResourceDefinitionBuilder<TBuilder>
{
    private readonly Dictionary<ResourceAttributeId, ResourceAttributeValue> _attributes = [];
    private readonly List<ResourceReference> _dependencies = [];
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

    protected IReadOnlyDictionary<ResourceCapabilityId, JsonElement> Capabilities => _capabilities;

    protected IReadOnlyDictionary<ResourceOperationId, JsonElement> Operations => _operations;

    protected ResourceGraphBuilder? ResourceGraph => _resourceGraph;

    public string EffectiveResourceId =>
        string.IsNullOrWhiteSpace(_resourceId)
            ? ResourceIdConventionResolver.Resolve(_resourceIdConvention, Name, TypeId, ProviderId)
            : _resourceId;

    public IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeValue> AttributeValues =>
        _attributes;

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

    public void SetAttribute(ResourceAttributeId attributeId, ResourceAttributeValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _attributes[attributeId] = value;
    }

    void IResourceDefinitionAttributeBuilder.RemoveAttribute(ResourceAttributeId attributeId) =>
        _attributes.Remove(attributeId);

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

    protected TBuilder RemoveAttribute(ResourceAttributeId attributeId)
    {
        _attributes.Remove(attributeId);
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
    private readonly Dictionary<ResourceClassId, ResourceClassDefinition> _resourceClassDefinitions = [];
    private readonly Dictionary<ResourceTypeId, ResourceTypeDefinition> _resourceTypeDefinitions = [];
    private readonly Dictionary<ResourceCapabilityId, IResourceCapabilityAttributeProvider>
        _resourceCapabilityAttributeProviders = [];
    private readonly Dictionary<string, ResourceIdentityDeclaration> _resourceIdentities =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ResourceAccessGrantDeclaration> _permissionGrants = [];
    private readonly IResourceIdConvention _resourceIdConvention =
        resourceIdConvention ?? DefaultResourceIdConvention.Instance;

    public IReadOnlyList<IResourceDefinitionBuilder> ResourceBuilders => _resources;

    public IReadOnlyDictionary<ResourceClassId, ResourceClassDefinition> ResourceClassDefinitions =>
        _resourceClassDefinitions;

    public IReadOnlyDictionary<ResourceTypeId, ResourceTypeDefinition> ResourceTypeDefinitions =>
        _resourceTypeDefinitions;

    public IReadOnlyDictionary<ResourceCapabilityId, IResourceCapabilityAttributeProvider>
        ResourceCapabilityAttributeProviders => _resourceCapabilityAttributeProviders;

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

    public ResourceGraphBuilder AddResourceTypeDefinition(ResourceTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _resourceTypeDefinitions[definition.TypeId] = definition;
        return this;
    }

    public ResourceGraphBuilder AddResourceClassDefinition(ResourceClassDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _resourceClassDefinitions[definition.ClassId] = definition;
        return this;
    }

    public ResourceGraphBuilder AddResourceCapabilityAttributeProvider(
        IResourceCapabilityAttributeProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        _resourceCapabilityAttributeProviders[provider.CapabilityId] = provider;
        return this;
    }

    public ResourceDefinitionSchemaCatalog CreateSchemaCatalog() =>
        new(
            ResourceTypeDefinitions.Values,
            ResourceCapabilityAttributeProviders.Values,
            ResourceClassDefinitions.Values);

    public ResourceGraphBuilder Add(ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return Add(new FixedResourceDefinitionBuilder(definition));
    }

    public ResourceGraphBuilder AddResourceIdentity(
        string resourceId,
        ResourceIdentityBindingAttribute identity,
        bool? provisionIdentityOnStartup = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(identity);

        _resourceIdentities[resourceId.Trim()] = new ResourceIdentityDeclaration(
            identity,
            provisionIdentityOnStartup);
        return this;
    }

    public ResourceGraphBuilder ProvisionIdentityOnStartup(
        string resourceId,
        bool provision = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var normalizedResourceId = resourceId.Trim();
        _resourceIdentities[normalizedResourceId] =
            _resourceIdentities.TryGetValue(normalizedResourceId, out var existing)
                ? existing with { ProvisionIdentityOnStartup = provision }
                : new ResourceIdentityDeclaration(null, provision);
        return this;
    }

    public ResourceGraphBuilder AddPermissionGrant(
        string targetResourceId,
        ResourceAccessGrantAttribute grant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetResourceId);
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(grant.Principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(grant.Permission);

        var normalized = new ResourceAccessGrantDeclaration(
            targetResourceId.Trim(),
            grant with
            {
                Permission = grant.Permission.Trim()
            });
        if (_permissionGrants.Any(existing =>
                string.Equals(existing.TargetResourceId, normalized.TargetResourceId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Grant.Permission, normalized.Grant.Permission, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Grant.Principal.Kind, normalized.Grant.Principal.Kind, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Grant.Principal.Id, normalized.Grant.Principal.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Grant.Principal.ProviderId, normalized.Grant.Principal.ProviderId, StringComparison.OrdinalIgnoreCase)))
        {
            return this;
        }

        _permissionGrants.Add(normalized);
        return this;
    }

    public ResourceGraphBuilder AddPermissionGrant(
        string targetResourceId,
        ResourcePrincipalReferenceAttribute principal,
        string permission) =>
        AddPermissionGrant(
            targetResourceId,
            new ResourceAccessGrantAttribute(principal, permission));

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

        var duplicate = definitions
            .GroupBy(resource => resource.EffectiveResourceId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Resource '{duplicate.Key}' is already defined in the graph.");
        }

        return new ResourceDefinitionGraph(
            definitions
                .Select(ApplyResourceMetadata)
                .ToArray());
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

    private ResourceDefinition ApplyResourceMetadata(ResourceDefinition definition)
    {
        var resourceId = definition.EffectiveResourceId;
        var attributes = definition.GetDeclarationAttributes();
        var identity = attributes.Identity;
        var provisionIdentityOnStartup = attributes.ProvisionIdentityOnStartup;
        if (_resourceIdentities.TryGetValue(resourceId, out var identityDeclaration))
        {
            identity = identityDeclaration.Identity ?? identity;
            provisionIdentityOnStartup =
                identityDeclaration.ProvisionIdentityOnStartup ??
                provisionIdentityOnStartup;
        }

        var declaredGrants = _permissionGrants
            .Where(grant => string.Equals(
                grant.TargetResourceId,
                resourceId,
                StringComparison.OrdinalIgnoreCase))
            .Select(grant => grant.Grant);
        var grants = attributes.AccessGrantsOrEmpty
            .Concat(declaredGrants)
            .DistinctBy(grant => new PermissionGrantKey(grant))
            .ToArray();

        return definition.WithDeclarationAttributes(
            identity,
            provisionIdentityOnStartup,
            grants);
    }

    private sealed record ResourceIdentityDeclaration(
        ResourceIdentityBindingAttribute? Identity,
        bool? ProvisionIdentityOnStartup);

    private sealed record ResourceAccessGrantDeclaration(
        string TargetResourceId,
        ResourceAccessGrantAttribute Grant);

    private sealed record PermissionGrantKey(
        string Kind,
        string Id,
        string? ProviderId,
        string Permission)
    {
        public PermissionGrantKey(ResourceAccessGrantAttribute grant)
            : this(
                grant.Principal.Kind,
                grant.Principal.Id,
                grant.Principal.ProviderId,
                grant.Permission)
        {
        }
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
