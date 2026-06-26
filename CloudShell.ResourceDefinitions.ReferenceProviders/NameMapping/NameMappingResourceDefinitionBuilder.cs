namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class NameMappingResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<NameMappingResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        NameMappingResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        NameMappingResourceTypeProvider.ProviderId;

    public NameMappingResourceDefinitionBuilder WithHostName(string hostName) =>
        SetScalarAttribute(NameMappingResourceTypeProvider.Attributes.HostName, hostName);

    public NameMappingResourceDefinitionBuilder WithTargetEndpointName(string endpointName) =>
        SetScalarAttribute(NameMappingResourceTypeProvider.Attributes.TargetEndpointName, endpointName);

    public NameMappingResourceDefinitionBuilder WithExposure(string exposure) =>
        SetScalarAttribute(NameMappingResourceTypeProvider.Attributes.Exposure, exposure);

    public NameMappingResourceDefinitionBuilder InDnsZone(IResourceDefinitionBuilder zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        return InDnsZone(zone.EffectiveResourceId);
    }

    public NameMappingResourceDefinitionBuilder InDnsZone(string zoneResourceId) =>
        AddDependency(ResourceReference.DependsOnResourceId(
            zoneResourceId,
            DnsZoneResourceTypeProvider.ResourceTypeId));

    public NameMappingResourceDefinitionBuilder MapsTarget(
        IResourceDefinitionBuilder target,
        ResourceTypeId? typeId = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        return MapsTarget(target.EffectiveResourceId, typeId);
    }

    public NameMappingResourceDefinitionBuilder MapsTarget(
        string targetResourceId,
        ResourceTypeId? typeId = null) =>
        AddDependency(ResourceReference.DependsOnResourceId(targetResourceId, typeId));
}

public static class NameMappingResourceDefinitionBuilderExtensions
{
    public static NameMappingResourceDefinitionBuilder AddNameMapping(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new NameMappingResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
