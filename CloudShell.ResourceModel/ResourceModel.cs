using System.Globalization;
using System.Text.Json;

namespace CloudShell.ResourceModel;

public sealed record ResourceState(
    string Name,
    ResourceTypeId TypeId,
    string? ResourceId = null,
    string? ProviderId = null,
    string? DisplayName = null,
    string? Version = null,
    IReadOnlyList<ResourceReference>? DependsOn = null,
    ResourceAttributeValueMap? Attributes = null,
    IReadOnlyDictionary<string, JsonElement>? Configuration = null,
    IReadOnlyDictionary<ResourceCapabilityId, JsonElement>? Capabilities = null,
    IReadOnlyDictionary<ResourceOperationId, JsonElement>? Operations = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? LastModifiedAt = null)
{
    private static readonly IReadOnlyList<ResourceReference> EmptyReferences = [];
    private static readonly ResourceAttributeValueMap EmptyAttributeValues =
        new(new Dictionary<ResourceAttributeId, ResourceAttributeValue>());
    private static readonly IReadOnlyDictionary<string, JsonElement> EmptyConfigurationPayloads =
        new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<ResourceCapabilityId, JsonElement> EmptyCapabilityPayloads =
        new Dictionary<ResourceCapabilityId, JsonElement>();
    private static readonly IReadOnlyDictionary<ResourceOperationId, JsonElement> EmptyOperationPayloads =
        new Dictionary<ResourceOperationId, JsonElement>();

    public string EffectiveResourceId =>
        string.IsNullOrWhiteSpace(ResourceId) ? $"{TypeId}:{Name}" : ResourceId;

    public IReadOnlyList<ResourceReference> StartupDependencies => DependsOn ?? EmptyReferences;

    public IReadOnlyList<string> StartupDependencyIds => StartupDependencies
        .Where(dependency => dependency.TryGetDependsOnResourceId(out _))
        .Select(dependency =>
        {
            dependency.TryGetDependsOnResourceId(out var resourceId);
            return resourceId;
        })
        .ToArray();

    public IReadOnlyList<ResourceReference> ResourceDependencies => StartupDependencies;

    public IReadOnlyList<string> ResourceDependencyIds => StartupDependencyIds;

    public ResourceAttributeValueMap ResourceAttributeValues => Attributes ?? EmptyAttributeValues;

    public IReadOnlyDictionary<ResourceAttributeId, string> ResourceAttributes =>
        ResourceAttributeValueMaps.ToScalars(Attributes);

    public IReadOnlyDictionary<string, JsonElement> ConfigurationPayloads => Configuration ?? EmptyConfigurationPayloads;

    public IReadOnlyDictionary<ResourceCapabilityId, JsonElement> CapabilityPayloads =>
        Capabilities ?? EmptyCapabilityPayloads;

    public IReadOnlyDictionary<ResourceOperationId, JsonElement> OperationPayloads =>
        Operations ?? EmptyOperationPayloads;

    public ResourceRevision Revision => ResourceRevision.Parse(Version);

    public ResourceState WithRevision(ResourceRevision revision) =>
        this with { Version = revision.ToString() };

    public static ResourceState FromDefinition(ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new(
            definition.Name,
            definition.TypeId,
            definition.ResourceId,
            definition.ProviderId,
            definition.DisplayName,
            definition.Version,
            definition.DependsOn,
            definition.Attributes,
            definition.Configuration,
            definition.Capabilities,
            definition.Operations,
            definition.Metadata);
    }

    public ResourceState ApplyDefinition(ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return this with
        {
            Name = Name,
            TypeId = TypeId,
            ResourceId = ResourceId,
            ProviderId = definition.ProviderId ?? ProviderId,
            DisplayName = definition.DisplayName ?? DisplayName,
            Version = definition.Version ?? Version,
            DependsOn = definition.DependsOn ?? DependsOn,
            Attributes = Merge(ResourceAttributeValues, definition.Attributes),
            Configuration = MergePayloads(ConfigurationPayloads, definition.Configuration),
            Capabilities = MergePayloads(CapabilityPayloads, definition.Capabilities),
            Operations = MergePayloads(OperationPayloads, definition.Operations),
            Metadata = Merge(Metadata, definition.Metadata),
            CreatedAt = CreatedAt,
            LastModifiedAt = LastModifiedAt
        };
    }

    public ResourceDefinition ToDefinition() =>
        new(
            Name,
            TypeId,
            ResourceId,
            ProviderId,
            DisplayName,
            Version,
            StartupDependencies,
            ResourceAttributeValues,
            ConfigurationPayloads,
            CapabilityPayloads,
            OperationPayloads,
            Metadata);

    public TConfiguration? GetConfiguration<TConfiguration>(
        string sectionName,
        JsonSerializerOptions? options = null) =>
        ConfigurationPayloads.TryGetValue(sectionName, out var payload)
            ? payload.Deserialize<TConfiguration>(options)
            : default;

    public TCapability? GetCapability<TCapability>(
        ResourceCapabilityId capabilityId,
        JsonSerializerOptions? options = null) =>
        CapabilityPayloads.TryGetValue(capabilityId, out var payload)
            ? payload.Deserialize<TCapability>(options)
            : default;

    public TOperation? GetOperation<TOperation>(
        ResourceOperationId operationId,
        JsonSerializerOptions? options = null) =>
        OperationPayloads.TryGetValue(operationId, out var payload)
            ? payload.Deserialize<TOperation>(options)
            : default;

    private static IReadOnlyDictionary<TKey, TValue>? Merge<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue>? current,
        IReadOnlyDictionary<TKey, TValue>? changes)
        where TKey : notnull
    {
        if (changes is null)
        {
            return current;
        }

        var merged = current is null
            ? new Dictionary<TKey, TValue>()
            : new Dictionary<TKey, TValue>(current);
        foreach (var (key, value) in changes)
        {
            merged[key] = value;
        }

        return merged;
    }

    private static ResourceAttributeValueMap? Merge(
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeValue>? current,
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeValue>? changes)
    {
        var merged = Merge<ResourceAttributeId, ResourceAttributeValue>(current, changes);
        return merged is null ? null : new ResourceAttributeValueMap(merged);
    }

    private static IReadOnlyDictionary<TKey, JsonElement>? MergePayloads<TKey>(
        IReadOnlyDictionary<TKey, JsonElement>? current,
        IReadOnlyDictionary<TKey, JsonElement>? changes)
        where TKey : notnull
    {
        if (changes is null)
        {
            return current;
        }

        var merged = current is null
            ? new Dictionary<TKey, JsonElement>()
            : current.ToDictionary(
                payload => payload.Key,
                payload => ResourceDefinitionJson.Clone(payload.Value));
        foreach (var (key, value) in changes)
        {
            merged[key] = ResourceDefinitionJson.Clone(value);
        }

        return merged;
    }
}

public readonly record struct ResourceRevision(long Value)
{
    public static ResourceRevision Initial { get; } = new(0);

    public ResourceRevision Next() => new(Value + 1);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public static ResourceRevision Parse(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var revision) && revision >= 0
            ? new(revision)
            : Initial;
}

public sealed record ResourceClass(
    ResourceClassDefinition Definition,
    ResourceAttributeSet Attributes,
    ResourceCapabilitySet Capabilities,
    ResourceOperationSet Operations)
{
    public ResourceClassId ClassId => Definition.ClassId;
}

public sealed record ResourceType(
    ResourceTypeDefinition Definition,
    ResourceClass Class,
    ResourceAttributeSet Attributes,
    ResourceCapabilitySet Capabilities,
    ResourceOperationSet Operations)
{
    public ResourceTypeId TypeId => Definition.TypeId;

    public ResourceClassId ClassId => Definition.ClassId;
}

public sealed record Resource(
    ResourceState State,
    ResourceClass Class,
    ResourceType Type,
    ResourceAttributeSet Attributes,
    ResourceCapabilitySet Capabilities,
    ResourceOperationSet Operations,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    private ResourceChangeContext? _pendingChanges;

    public string Name => State.Name;

    public string EffectiveResourceId => State.EffectiveResourceId;

    public string? Version => State.Version;

    public ResourceRevision Revision => State.Revision;

    public DateTimeOffset? CreatedAt => State.CreatedAt;

    public DateTimeOffset? LastModifiedAt => State.LastModifiedAt;

    public bool HasPendingChanges => _pendingChanges?.HasChanges == true;

    public ResourceChangeContext CreateChangeContext() => new(this);

    public void SetAttribute(
        ResourceAttributeId name,
        string value) =>
        SetAttribute(name, ResourceAttributeValue.String(value));

    public void SetAttribute(
        ResourceAttributeId name,
        int value) =>
        SetAttribute(name, ResourceAttributeValue.Integer(value));

    public void SetAttribute(
        ResourceAttributeId name,
        ResourceAttributeValue value) =>
        PendingChanges.SetAttribute(name, value);

    public ResourceChangeSet GetPendingChanges() =>
        _pendingChanges?.GetChanges() ?? ResourceChangeSet.Empty(this);

    public ResourceChangeSet ApplyChanges()
    {
        var changes = GetPendingChanges();
        _pendingChanges?.Dispose();
        _pendingChanges = null;
        return changes;
    }

    public ResourceChangeSet ApplyDefinition(ResourceDefinition definition) =>
        ResourceChangeSet.FromDefinition(this, definition);

    public ResourceDefinition ToDefinition(bool includePendingChanges = false) =>
        includePendingChanges
            ? GetPendingChanges().ToDefinition()
            : ToDefinition(State);

    public TConfiguration? GetConfiguration<TConfiguration>(
        string sectionName,
        JsonSerializerOptions? options = null) =>
        State.GetConfiguration<TConfiguration>(sectionName, options);

    public TCapability? GetCapability<TCapability>(
        ResourceCapabilityId capabilityId,
        JsonSerializerOptions? options = null) =>
        State.GetCapability<TCapability>(capabilityId, options);

    public TOperation? GetOperation<TOperation>(
        ResourceOperationId operationId,
        JsonSerializerOptions? options = null) =>
        State.GetOperation<TOperation>(operationId, options);

    private ResourceChangeContext PendingChanges =>
        _pendingChanges ??= CreateChangeContext();

    private ResourceDefinition ToDefinition(ResourceState state) =>
        state.ToDefinition() with
        {
            Attributes = FilterInterchangeAttributes(state.ResourceAttributeValues)
        };

    internal bool IsReadOnlyAttribute(ResourceAttributeId attributeId) =>
        GetReadOnlyAttributePolicy(attributeId) ??
        Attributes.Resolve(attributeId)?.ReadOnly == true;

    internal ResourceAttributeMutability GetAttributeMutability(ResourceAttributeId attributeId) =>
        GetAttributeMutabilityPolicy(attributeId) ??
        Attributes.Resolve(attributeId)?.Mutability ??
        ResourceAttributeMutability.CallerManaged;

    internal ResourceAttributeValueMap? FilterInterchangeAttributes(
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeValue> attributes)
    {
        var filtered = attributes
            .Where(attribute => !IsReadOnlyAttribute(attribute.Key))
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value);

        return filtered.Count == 0 ? null : new ResourceAttributeValueMap(filtered);
    }

    private bool? GetReadOnlyAttributePolicy(ResourceAttributeId attributeId)
    {
        var classReadOnly = TryGetReadOnly(Class.Definition.Attributes, attributeId);
        var typeReadOnly = TryGetReadOnly(Type.Definition.Attributes, attributeId);
        return typeReadOnly ?? classReadOnly;
    }

    private ResourceAttributeMutability? GetAttributeMutabilityPolicy(ResourceAttributeId attributeId)
    {
        var classMutability = TryGetMutability(Class.Definition.Attributes, attributeId);
        var typeMutability = TryGetMutability(Type.Definition.Attributes, attributeId);
        return typeMutability ?? classMutability;
    }

    private static bool? TryGetReadOnly(
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? attributeDefinitions,
        ResourceAttributeId attributeId) =>
        attributeDefinitions is not null &&
        attributeDefinitions.TryGetValue(attributeId, out var attributeDefinition) &&
        attributeDefinition.ReadOnly.HasValue
            ? attributeDefinition.ReadOnly
            : null;

    private static ResourceAttributeMutability? TryGetMutability(
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? attributeDefinitions,
        ResourceAttributeId attributeId) =>
        attributeDefinitions is not null &&
        attributeDefinitions.TryGetValue(attributeId, out var attributeDefinition) &&
        attributeDefinition.Mutability.HasValue
            ? attributeDefinition.Mutability
            : null;
}

public sealed class ResourceChangeContext(
    Resource resource) : IDisposable
{
    private readonly Dictionary<ResourceAttributeId, ResourceAttributeValue> _attributeChanges = [];
    private readonly Dictionary<ResourceCapabilityId, JsonElement> _capabilityChanges = [];
    private bool _isDisposed;

    public Resource Resource { get; } = resource;

    public bool HasChanges => _attributeChanges.Count > 0 || _capabilityChanges.Count > 0;

    public void SetAttribute(
        ResourceAttributeId name,
        string value)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _attributeChanges[name] = ResourceAttributeValue.String(value);
    }

    public void SetAttribute(
        ResourceAttributeId name,
        ResourceAttributeValue value)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _attributeChanges[name] = value;
    }

    public void SetAttribute(
        ResourceAttributeId name,
        int value) =>
        SetAttribute(name, ResourceAttributeValue.Integer(value));

    public void SetCapability(
        ResourceCapabilityId capabilityId,
        JsonElement payload)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _capabilityChanges[capabilityId] = ResourceDefinitionJson.Clone(payload);
    }

    public ResourceChangeSet ApplyChanges()
    {
        var changes = GetChanges();
        _attributeChanges.Clear();
        _capabilityChanges.Clear();
        return changes;
    }

    public ResourceChangeSet GetChanges()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>(
            Resource.State.ResourceAttributeValues);
        var capabilities = Resource.State.CapabilityPayloads.ToDictionary(
            pair => pair.Key,
            pair => ResourceDefinitionJson.Clone(pair.Value));
        var attributeChanges = new List<ResourceAttributeChange>();
        var capabilityChanges = new List<ResourceCapabilityChange>();

        foreach (var (name, newValue) in _attributeChanges)
        {
            attributes.TryGetValue(name, out var previousValue);
            attributes[name] = newValue;
            attributeChanges.Add(new(name, previousValue, newValue));
        }

        foreach (var (capabilityId, newPayload) in _capabilityChanges)
        {
            var previousPayload = capabilities.TryGetValue(capabilityId, out var currentPayload)
                ? ResourceDefinitionJson.Clone(currentPayload)
                : (JsonElement?)null;
            capabilities[capabilityId] = ResourceDefinitionJson.Clone(newPayload);
            capabilityChanges.Add(new(
                capabilityId,
                previousPayload,
                ResourceDefinitionJson.Clone(newPayload)));
        }

        return new(
            Resource,
            Resource.State with
            {
                Attributes = new ResourceAttributeValueMap(attributes),
                Capabilities = capabilities
            },
            attributeChanges,
            capabilityChanges,
            []);
    }

    public void Dispose()
    {
        _isDisposed = true;
    }
}

public sealed record ResourceChangeSet(
    Resource Resource,
    ResourceState ProposedState,
    IReadOnlyList<ResourceAttributeChange> AttributeChanges,
    IReadOnlyList<ResourceCapabilityChange> CapabilityChanges,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool IsNewResource { get; init; }

    public bool HasChanges => IsNewResource || AttributeChanges.Count > 0 || CapabilityChanges.Count > 0;

    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);

    public ResourceDefinition ToDefinition() =>
        ProposedState.ToDefinition() with
        {
            Attributes = Resource.FilterInterchangeAttributes(ProposedState.ResourceAttributeValues)
        };

    public ResourceDefinition ToIncrementalDefinition() =>
        IsNewResource
            ? ToDefinition()
            : new(
                Resource.State.Name,
                Resource.State.TypeId,
                Resource.State.ResourceId,
                Resource.State.ProviderId,
                Resource.State.DisplayName,
                Resource.State.Version,
                Attributes: AttributeChanges.ToDictionary(
                    change => change.AttributeId,
                    change => change.NewValue)
                    .Where(attribute => !Resource.IsReadOnlyAttribute(attribute.Key))
                    .ToDictionary(attribute => attribute.Key, attribute => attribute.Value),
                Capabilities: CapabilityChanges.ToDictionary(
                    change => change.CapabilityId,
                    change => ResourceDefinitionJson.Clone(change.NewPayload)));

    public static ResourceChangeSet Empty(Resource resource) =>
        new(resource, resource.State, [], [], []);

    public static ResourceChangeSet FromNewResource(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return new(
            resource,
            resource.State,
            resource.State.ResourceAttributeValues
                .Select(change => new ResourceAttributeChange(
                    change.Key,
                    PreviousValue: null,
                    change.Value))
                .ToArray(),
            resource.State.CapabilityPayloads
                .Select(change => new ResourceCapabilityChange(
                    change.Key,
                    PreviousPayload: null,
                    ResourceDefinitionJson.Clone(change.Value)))
                .ToArray(),
            resource.Diagnostics)
        {
            IsNewResource = true
        };
    }

    public static ResourceChangeSet FromDefinition(
        Resource resource,
        ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(definition);

        var targetDiagnostic = ValidateDefinitionTarget(resource, definition);
        if (targetDiagnostic is not null)
        {
            return new(
                resource,
                resource.State,
                [],
                [],
                [targetDiagnostic]);
        }

        var proposedState = resource.State.ApplyDefinition(definition);
        var attributeChanges = definition.Attributes is null
            ? Array.Empty<ResourceAttributeChange>()
            : definition.Attributes
                .Where(change =>
                    !resource.State.ResourceAttributeValues.TryGetValue(change.Key, out var currentValue) ||
                    !ResourceAttributeValueEquals(currentValue, change.Value))
                .Select(change =>
                {
                    resource.State.ResourceAttributeValues.TryGetValue(change.Key, out var previousValue);
                    return new ResourceAttributeChange(
                        change.Key,
                        previousValue,
                        change.Value);
                })
                .ToArray();
        var capabilityChanges = definition.Capabilities is null
            ? Array.Empty<ResourceCapabilityChange>()
            : definition.Capabilities
                .Where(change =>
                    !resource.State.CapabilityPayloads.TryGetValue(change.Key, out var currentPayload) ||
                    !JsonPayloadEquals(currentPayload, change.Value))
                .Select(change =>
                {
                    var previousPayload = resource.State.CapabilityPayloads.TryGetValue(
                        change.Key,
                        out var currentPayload)
                            ? ResourceDefinitionJson.Clone(currentPayload)
                            : (JsonElement?)null;

                    return new ResourceCapabilityChange(
                        change.Key,
                        previousPayload,
                        ResourceDefinitionJson.Clone(change.Value));
                })
                .ToArray();

        return new(
            resource,
            proposedState,
            attributeChanges,
            capabilityChanges,
            []);
    }

    private static bool JsonPayloadEquals(
        JsonElement left,
        JsonElement right) =>
        string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);

    private static bool ResourceAttributeValueEquals(
        ResourceAttributeValue left,
        ResourceAttributeValue right) =>
        string.Equals(
            JsonSerializer.Serialize(left, JsonSerializerOptions.Web),
            JsonSerializer.Serialize(right, JsonSerializerOptions.Web),
            StringComparison.Ordinal);

    private static ResourceDefinitionDiagnostic? ValidateDefinitionTarget(
        Resource resource,
        ResourceDefinition definition)
    {
        if (resource.Type.TypeId != definition.TypeId)
        {
            return ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.ResourceDefinitionTargetMismatch,
                $"Resource definition '{definition.EffectiveResourceId}' cannot be applied to resource '{resource.EffectiveResourceId}'.",
                definition.EffectiveResourceId);
        }

        if (!string.IsNullOrWhiteSpace(definition.ResourceId))
        {
            if (!string.Equals(resource.EffectiveResourceId, definition.ResourceId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceDefinitionTargetMismatch,
                    $"Resource definition '{definition.EffectiveResourceId}' cannot be applied to resource '{resource.EffectiveResourceId}'.",
                    definition.EffectiveResourceId);
            }

            if (!string.Equals(resource.Name, definition.Name, StringComparison.OrdinalIgnoreCase))
            {
                return ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceDefinitionIdentityChangeNotAllowed,
                    $"Resource definition '{definition.EffectiveResourceId}' cannot rename resource '{resource.EffectiveResourceId}' from '{resource.Name}' to '{definition.Name}'.",
                    resource.EffectiveResourceId);
            }

            return null;
        }

        return string.Equals(resource.Name, definition.Name, StringComparison.OrdinalIgnoreCase)
            ? null
            : ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.ResourceDefinitionTargetMismatch,
                $"Resource definition '{definition.EffectiveResourceId}' cannot be applied to resource '{resource.EffectiveResourceId}'.",
                definition.EffectiveResourceId);
    }
}

public sealed record ResourceAttributeChange(
    ResourceAttributeId AttributeId,
    ResourceAttributeValue? PreviousValue,
    ResourceAttributeValue NewValue);

public sealed record ResourceCapabilityChange(
    ResourceCapabilityId CapabilityId,
    JsonElement? PreviousPayload,
    JsonElement NewPayload);
