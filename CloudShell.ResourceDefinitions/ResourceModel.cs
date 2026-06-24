using System.Globalization;
using System.Text.Json;

namespace CloudShell.ResourceDefinitions;

public sealed record ResourceState(
    string Name,
    ResourceTypeId TypeId,
    string? ResourceId = null,
    string? ProviderId = null,
    string? DisplayName = null,
    string? Version = null,
    IReadOnlyList<string>? DependsOn = null,
    IReadOnlyDictionary<ResourceAttributeId, string>? Attributes = null,
    IReadOnlyDictionary<string, JsonElement>? Configuration = null,
    IReadOnlyDictionary<ResourceCapabilityId, JsonElement>? Capabilities = null,
    IReadOnlyDictionary<ResourceOperationId, JsonElement>? Operations = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? LastModifiedAt = null)
{
    private static readonly IReadOnlyList<string> EmptyList = [];
    private static readonly IReadOnlyDictionary<ResourceAttributeId, string> EmptyAttributes =
        new Dictionary<ResourceAttributeId, string>();
    private static readonly IReadOnlyDictionary<string, JsonElement> EmptyConfigurationPayloads =
        new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<ResourceCapabilityId, JsonElement> EmptyCapabilityPayloads =
        new Dictionary<ResourceCapabilityId, JsonElement>();
    private static readonly IReadOnlyDictionary<ResourceOperationId, JsonElement> EmptyOperationPayloads =
        new Dictionary<ResourceOperationId, JsonElement>();

    public string EffectiveResourceId =>
        string.IsNullOrWhiteSpace(ResourceId) ? $"{TypeId}:{Name}" : ResourceId;

    public IReadOnlyList<string> ResourceDependencies => DependsOn ?? EmptyList;

    public IReadOnlyDictionary<ResourceAttributeId, string> ResourceAttributes => Attributes ?? EmptyAttributes;

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

    public ResourceState ApplyDefinition(ResourceDefinition definition) =>
        FromDefinition(definition);

    public ResourceDefinition ToDefinition() =>
        new(
            Name,
            TypeId,
            ResourceId,
            ProviderId,
            DisplayName,
            Version,
            ResourceDependencies,
            ResourceAttributes,
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
        PendingChanges.SetAttribute(name, value);

    public void SetAttribute(
        ResourceAttributeId name,
        int value) =>
        SetAttribute(name, value.ToString(CultureInfo.InvariantCulture));

    public ResourceChangeSet GetPendingChanges() =>
        _pendingChanges?.GetChanges() ?? ResourceChangeSet.Empty(this);

    public ResourceChangeSet ApplyChanges()
    {
        var changes = GetPendingChanges();
        _pendingChanges?.Dispose();
        _pendingChanges = null;
        return changes;
    }

    public ResourceDefinition ToDefinition(bool includePendingChanges = false) =>
        includePendingChanges
            ? GetPendingChanges().ToDefinition()
            : State.ToDefinition();

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
}

public sealed class ResourceChangeContext(
    Resource resource) : IDisposable
{
    private readonly Dictionary<ResourceAttributeId, string> _attributeChanges = [];
    private readonly Dictionary<ResourceCapabilityId, JsonElement> _capabilityChanges = [];
    private bool _isDisposed;

    public Resource Resource { get; } = resource;

    public bool HasChanges => _attributeChanges.Count > 0 || _capabilityChanges.Count > 0;

    public void SetAttribute(
        ResourceAttributeId name,
        string value)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _attributeChanges[name] = value;
    }

    public void SetAttribute(
        ResourceAttributeId name,
        int value) =>
        SetAttribute(name, value.ToString(CultureInfo.InvariantCulture));

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

        var attributes = new Dictionary<ResourceAttributeId, string>(
            Resource.State.ResourceAttributes);
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
                Attributes = attributes,
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
    public bool HasChanges => AttributeChanges.Count > 0 || CapabilityChanges.Count > 0;

    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);

    public ResourceDefinition ToDefinition() => ProposedState.ToDefinition();

    public ResourceDefinition ToIncrementalDefinition() =>
        new(
            Resource.State.Name,
            Resource.State.TypeId,
            Resource.State.ResourceId,
            Resource.State.ProviderId,
            Resource.State.DisplayName,
            Resource.State.Version,
            Attributes: AttributeChanges.ToDictionary(
                change => change.AttributeId,
                change => change.NewValue),
            Capabilities: CapabilityChanges.ToDictionary(
                change => change.CapabilityId,
                change => ResourceDefinitionJson.Clone(change.NewPayload)));

    public static ResourceChangeSet Empty(Resource resource) =>
        new(resource, resource.State, [], [], []);
}

public sealed record ResourceAttributeChange(
    ResourceAttributeId AttributeId,
    string? PreviousValue,
    string NewValue);

public sealed record ResourceCapabilityChange(
    ResourceCapabilityId CapabilityId,
    JsonElement? PreviousPayload,
    JsonElement NewPayload);
