using System.Text.Json;

namespace CloudShell.ResourceDefinitions;

public sealed class ResourceAttributeSet : IReadOnlyCollection<ResourceAttributeResolution>
{
    private readonly IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeResolution> _attributes;

    public ResourceAttributeSet(IEnumerable<ResourceAttributeResolution> attributes)
    {
        _attributes = attributes.ToDictionary(
            attribute => attribute.Name);
    }

    public int Count => _attributes.Count;

    public bool Has(ResourceAttributeId name) => _attributes.ContainsKey(name);

    public string? GetString(ResourceAttributeId name) =>
        _attributes.TryGetValue(name, out var attribute) ? attribute.Value : null;

    public ResourceAttributeResolution? Resolve(ResourceAttributeId name) =>
        _attributes.GetValueOrDefault(name);

    public IEnumerator<ResourceAttributeResolution> GetEnumerator() =>
        _attributes.Values.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}

public sealed record ResourceAttributeResolution(
    ResourceAttributeId Name,
    string Value,
    ResourceDefinitionValueSource Source);

public sealed class ResourceCapabilitySet : IReadOnlyCollection<ResourceCapabilityResolution>
{
    private readonly IReadOnlyDictionary<ResourceCapabilityId, ResourceCapabilityResolution> _capabilities;
    private IReadOnlyList<IResourceCapabilityProjection> _projections = [];

    public ResourceCapabilitySet(IEnumerable<ResourceCapabilityResolution> capabilities)
    {
        _capabilities = capabilities.ToDictionary(
            capability => capability.Id);
    }

    public int Count => _capabilities.Count;

    public bool Has(ResourceCapabilityId capabilityId) => _capabilities.ContainsKey(capabilityId);

    public ResourceCapabilityResolution? Resolve(ResourceCapabilityId capabilityId) =>
        _capabilities.GetValueOrDefault(capabilityId);

    public TCapability? Get<TCapability>()
        where TCapability : class, IResourceCapabilityProjection =>
        _projections.OfType<TCapability>().FirstOrDefault();

    public TCapability? Get<TCapability>(
        ResourceCapabilityId capabilityId,
        JsonSerializerOptions? options = null) =>
        Resolve(capabilityId) is { } capability
            ? capability.Payload.Deserialize<TCapability>(options)
            : default;

    internal void SetProjections(IEnumerable<IResourceCapabilityProjection> projections) =>
        _projections = projections.ToArray();

    public IEnumerator<ResourceCapabilityResolution> GetEnumerator() =>
        _capabilities.Values.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}

public sealed record ResourceCapabilityResolution(
    ResourceCapabilityId Id,
    JsonElement Payload,
    ResourceDefinitionValueSource Source,
    bool IsRequired = false);

public sealed class ResourceOperationSet : IReadOnlyCollection<ResourceOperationResolution>
{
    private readonly IReadOnlyDictionary<ResourceOperationId, ResourceOperationResolution> _operations;
    private IReadOnlyList<IResourceOperationProjection> _projections = [];

    public ResourceOperationSet(IEnumerable<ResourceOperationResolution> operations)
    {
        _operations = operations.ToDictionary(
            operation => operation.Id);
    }

    public int Count => _operations.Count;

    public bool Has(ResourceOperationId operationId) => _operations.ContainsKey(operationId);

    public TOperation? Get<TOperation>()
        where TOperation : class, IResourceOperationProjection =>
        _projections.OfType<TOperation>().FirstOrDefault();

    public IResourceOperationProjection? Get(ResourceOperationId operationId) =>
        _projections.FirstOrDefault(projection => projection.OperationId == operationId);

    public ResourceOperationResolution Resolve(
        ResourceOperationId operationId,
        ResourceDefinitionValueSource? source = null)
    {
        if (_operations.TryGetValue(operationId, out var operation) &&
            (source is null || operation.Source == source))
        {
            return operation;
        }

        return ResourceOperationResolution.Unavailable(
            operationId,
            source ?? ResourceDefinitionValueSource.ResourceState,
            $"Operation '{operationId}' is not available.");
    }

    public IEnumerator<ResourceOperationResolution> GetEnumerator() =>
        _operations.Values.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();

    internal void SetProjections(IEnumerable<IResourceOperationProjection> projections) =>
        _projections = projections.ToArray();
}

public sealed record ResourceOperationResolution(
    ResourceOperationId Id,
    JsonElement Payload,
    ResourceDefinitionValueSource Source,
    bool IsEnabled,
    bool AllowOverride,
    string? UnavailableReason = null)
{
    public bool IsAvailable => IsEnabled && string.IsNullOrWhiteSpace(UnavailableReason);

    public static ResourceOperationResolution Unavailable(
        ResourceOperationId operationId,
        ResourceDefinitionValueSource source,
        string reason) =>
        new(
            operationId,
            ResourceDefinitionJson.EmptyObject,
            source,
            IsEnabled: false,
            AllowOverride: false,
            UnavailableReason: reason);
}
