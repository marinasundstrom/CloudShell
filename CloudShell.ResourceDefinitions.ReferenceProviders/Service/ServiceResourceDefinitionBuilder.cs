namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class ServiceResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<ServiceResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        ServiceResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        ServiceResourceTypeProvider.ProviderId;

    public ServiceResourceDefinitionBuilder WithRoutingMode(string routingMode) =>
        SetScalarAttribute(ServiceResourceTypeProvider.Attributes.RoutingMode, routingMode);

    public ServiceResourceDefinitionBuilder DependsOnTarget(
        IResourceDefinitionBuilder target,
        ResourceTypeId? typeId = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        return DependsOnTarget(target.EffectiveResourceId, typeId);
    }

    public ServiceResourceDefinitionBuilder DependsOnTarget(
        string targetResourceId,
        ResourceTypeId? typeId = null) =>
        AddDependency(ResourceReference.DependsOnResourceId(targetResourceId, typeId));

    public ServiceResourceDefinitionBuilder DependsOnNetwork(
        IResourceDefinitionBuilder network,
        ResourceTypeId? typeId = null)
    {
        ArgumentNullException.ThrowIfNull(network);

        return DependsOnNetwork(network.EffectiveResourceId, typeId);
    }

    public ServiceResourceDefinitionBuilder DependsOnNetwork(
        string networkResourceId,
        ResourceTypeId? typeId = null) =>
        AddDependency(ResourceReference.DependsOnResourceId(
            networkResourceId,
            typeId ?? NetworkResourceTypeProvider.ResourceTypeId));
}

public static class ServiceResourceDefinitionBuilderExtensions
{
    public static ServiceResourceDefinitionBuilder AddService(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new ServiceResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
