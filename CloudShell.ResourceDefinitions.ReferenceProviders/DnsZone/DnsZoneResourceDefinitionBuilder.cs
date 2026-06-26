namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class DnsZoneResourceDefinitionBuilder(string name) :
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
}

public static class DnsZoneResourceDefinitionBuilderExtensions
{
    public static DnsZoneResourceDefinitionBuilder AddDnsZone(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new DnsZoneResourceDefinitionBuilder(name)
            .WithZoneName(name);
        graph.Add(builder);
        return builder;
    }
}
