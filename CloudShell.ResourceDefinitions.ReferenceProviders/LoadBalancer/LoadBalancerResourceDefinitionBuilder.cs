namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class LoadBalancerResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<LoadBalancerResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        LoadBalancerResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        LoadBalancerResourceTypeProvider.ProviderId;

    public LoadBalancerResourceDefinitionBuilder WithProvider(string provider) =>
        SetScalarAttribute(LoadBalancerResourceTypeProvider.Attributes.Provider, provider);

    public LoadBalancerResourceDefinitionBuilder WithEntrypointCount(long count) =>
        SetScalarAttribute(LoadBalancerResourceTypeProvider.Attributes.EntrypointCount, count);

    public LoadBalancerResourceDefinitionBuilder WithRouteCount(long count) =>
        SetScalarAttribute(LoadBalancerResourceTypeProvider.Attributes.RouteCount, count);

    public LoadBalancerResourceDefinitionBuilder WithHttpRouteCount(long count) =>
        SetScalarAttribute(LoadBalancerResourceTypeProvider.Attributes.HttpRouteCount, count);

    public LoadBalancerResourceDefinitionBuilder WithTcpRouteCount(long count) =>
        SetScalarAttribute(LoadBalancerResourceTypeProvider.Attributes.TcpRouteCount, count);

    public LoadBalancerResourceDefinitionBuilder UseHost(
        IResourceDefinitionBuilder host,
        ResourceTypeId? typeId = null)
    {
        ArgumentNullException.ThrowIfNull(host);

        return UseHost(host.EffectiveResourceId, typeId);
    }

    public LoadBalancerResourceDefinitionBuilder UseHost(
        string hostResourceId,
        ResourceTypeId? typeId = null)
    {
        SetScalarAttribute(LoadBalancerResourceTypeProvider.Attributes.HostResourceId, hostResourceId);
        return AddDependency(ResourceReference.DependsOnResourceId(
            hostResourceId,
            typeId ?? DockerHostResourceTypeProvider.ResourceTypeId));
    }

    public LoadBalancerResourceDefinitionBuilder AddBackendTarget(
        IResourceDefinitionBuilder target,
        ResourceTypeId? typeId = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        return AddBackendTarget(target.EffectiveResourceId, typeId);
    }

    public LoadBalancerResourceDefinitionBuilder AddBackendTarget(
        string targetResourceId,
        ResourceTypeId? typeId = null) =>
        AddDependency(ResourceReference.DependsOnResourceId(targetResourceId, typeId));
}

public static class LoadBalancerResourceDefinitionBuilderExtensions
{
    public static LoadBalancerResourceDefinitionBuilder AddLoadBalancer(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new LoadBalancerResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
