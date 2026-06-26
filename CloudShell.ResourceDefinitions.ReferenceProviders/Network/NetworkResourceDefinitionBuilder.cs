namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class NetworkResourceDefinitionBuilder(string name) : IResourceDefinitionBuilder
{
    private readonly Dictionary<ResourceAttributeId, ResourceAttributeValue> _attributes = [];
    private string? _resourceId;
    private string? _displayName;

    public string Name { get; } = NormalizeName(name);

    public NetworkResourceDefinitionBuilder WithResourceId(string? resourceId)
    {
        _resourceId = string.IsNullOrWhiteSpace(resourceId) ? null : resourceId.Trim();
        return this;
    }

    public NetworkResourceDefinitionBuilder WithDisplayName(string? displayName)
    {
        _displayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        return this;
    }

    public NetworkResourceDefinitionBuilder WithNetworkKind(string networkKind)
    {
        SetScalarAttribute(NetworkResourceTypeProvider.Attributes.NetworkKind, networkKind);
        return this;
    }

    public NetworkResourceDefinitionBuilder WithHostReadiness(string hostReadiness)
    {
        SetScalarAttribute(NetworkResourceTypeProvider.Attributes.HostReadiness, hostReadiness);
        return this;
    }

    public NetworkResourceDefinitionBuilder WithMappingProviders(params string[] mappingProviders)
    {
        ArgumentNullException.ThrowIfNull(mappingProviders);

        SetScalarAttribute(
            NetworkResourceTypeProvider.Attributes.MappingProviders,
            string.Join(
                ",",
                mappingProviders
                    .Where(provider => !string.IsNullOrWhiteSpace(provider))
                    .Select(provider => provider.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)));
        return this;
    }

    public ResourceDefinition Build() =>
        new(
            Name,
            NetworkResourceTypeProvider.ResourceTypeId,
            ResourceId: _resourceId,
            ProviderId: NetworkResourceTypeProvider.ProviderId,
            DisplayName: _displayName,
            Attributes: _attributes.Count == 0
                ? null
                : new ResourceAttributeValueMap(_attributes));

    private void SetScalarAttribute(
        ResourceAttributeId attributeId,
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        _attributes[attributeId] = ResourceAttributeValue.String(value.Trim());
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }
}

public static class NetworkResourceDefinitionBuilderExtensions
{
    public static NetworkResourceDefinitionBuilder AddNetwork(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new NetworkResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
