namespace CloudShell.ControlPlane.Providers;

public sealed class DnsZoneResourceDefinitionBuilder(
    string name,
    ResourceDefinitionGraphBuilder? graph = null) :
    ResourceDefinitionBuilder<DnsZoneResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        DnsZoneResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        DnsZoneResourceTypeProvider.ProviderId;

    public DnsZoneResourceDefinitionBuilder WithZoneName(string zoneName) =>
        SetScalarAttribute(DnsZoneResourceTypeProvider.Attributes.ZoneName, zoneName);

    public DnsZoneResourceDefinitionBuilder WithProvider(string provider) =>
        SetScalarAttribute(DnsZoneResourceTypeProvider.Attributes.Provider, provider);

    public DnsZoneResourceDefinitionBuilder UseLocalHostNames() =>
        WithProvider(DnsZoneResourceTypeProvider.Providers.LocalHostNames);

    public NameMappingResourceDefinitionBuilder AddNameMapping(string name)
    {
        if (graph is null)
        {
            throw new InvalidOperationException(
                "The DNS zone builder is not attached to a resource graph builder.");
        }

        return graph
            .AddNameMapping(name)
            .InDnsZone(this);
    }

    public DnsZoneResourceDefinitionBuilder MapHost(
        string hostName,
        IResourceDefinitionBuilder target,
        string? endpointName = null,
        string? id = null,
        string? name = null,
        string exposure = "Public",
        Action<NameMappingResourceDefinitionBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);
        ArgumentNullException.ThrowIfNull(target);

        var normalizedHostName = hostName.Trim().ToLowerInvariant();
        var mappingName = string.IsNullOrWhiteSpace(name)
            ? CreateStableIdentifier(normalizedHostName)
            : name.Trim();
        var mapping = AddNameMapping(mappingName)
            .WithDisplayName(normalizedHostName)
            .WithHostName(normalizedHostName)
            .WithTargetEndpointName(string.IsNullOrWhiteSpace(endpointName)
                ? "http"
                : endpointName.Trim())
            .WithExposure(exposure)
            .MapsTarget(target);

        if (!string.IsNullOrWhiteSpace(id))
        {
            mapping.WithResourceId(id);
        }

        configure?.Invoke(mapping);

        return this;
    }

    private static string CreateStableIdentifier(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        var identifier = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(identifier) ? "name" : identifier;
    }
}

public static class DnsZoneResourceDefinitionBuilderExtensions
{
    public static DnsZoneResourceDefinitionBuilder AddDnsZone(
        this ResourceDefinitionGraphBuilder graph,
        string name,
        string? zoneName = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new DnsZoneResourceDefinitionBuilder(name, graph)
            .WithZoneName(string.IsNullOrWhiteSpace(zoneName)
                ? name
                : zoneName);
        graph.Add(builder);
        return builder;
    }
}
