namespace CloudShell.ControlPlane.Providers;

public sealed class LoadBalancerResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<LoadBalancerResourceDefinitionBuilder>(name)
{
    private readonly List<LoadBalancerEntrypointValue> _entrypoints = [];
    private readonly List<LoadBalancerRouteValue> _routes = [];

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

        return UseHost(host.EffectiveResourceId, typeId ?? host.ResourceTypeId);
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

        return AddBackendTarget(target.EffectiveResourceId, typeId ?? target.ResourceTypeId);
    }

    public LoadBalancerResourceDefinitionBuilder AddBackendTarget(
        string targetResourceId,
        ResourceTypeId? typeId = null) =>
        AddDependency(ResourceReference.DependsOnResourceId(targetResourceId, typeId));

    public LoadBalancerResourceDefinitionBuilder ExposeHttp(
        int port = 80,
        string name = "http",
        string exposure = "Public") =>
        AddEntrypoint(name, "Http", port, exposure);

    public LoadBalancerResourceDefinitionBuilder ExposeHttps(
        int port = 443,
        string name = "https",
        string exposure = "Public") =>
        AddEntrypoint(name, "Https", port, exposure);

    public LoadBalancerResourceDefinitionBuilder ExposeTcp(
        int port,
        string? name = null,
        string exposure = "Public") =>
        AddEntrypoint(name ?? $"tcp-{port}", "Tcp", port, exposure);

    public LoadBalancerResourceDefinitionBuilder MapHost(
        string host,
        IResourceDefinitionBuilder target,
        string endpoint = "http",
        string? id = null,
        string entrypoint = "http")
    {
        ArgumentNullException.ThrowIfNull(target);

        return AddRoute(
            "Http",
            id,
            $"{host} to {target.EffectiveResourceId}",
            entrypoint,
            new LoadBalancerRouteMatchValue(Host: host),
            new LoadBalancerRouteTargetValue(
                ResourceReference.ReferenceResourceId(
                    target.EffectiveResourceId,
                    target.ResourceTypeId,
                    target.ResourceProviderId),
                EndpointName: endpoint),
            target);
    }

    public LoadBalancerResourceDefinitionBuilder MapHost(
        string host,
        IResourceDefinitionBuilder target,
        int port,
        string? id = null,
        string entrypoint = "http")
    {
        ArgumentNullException.ThrowIfNull(target);

        return AddRoute(
            "Http",
            id,
            $"{host} to {target.EffectiveResourceId}:{port}",
            entrypoint,
            new LoadBalancerRouteMatchValue(Host: host),
            new LoadBalancerRouteTargetValue(
                ResourceReference.ReferenceResourceId(
                    target.EffectiveResourceId,
                    target.ResourceTypeId,
                    target.ResourceProviderId),
                Port: port),
            target);
    }

    public LoadBalancerResourceDefinitionBuilder MapPath(
        string host,
        string pathPrefix,
        IResourceDefinitionBuilder target,
        string endpoint = "http",
        string? id = null,
        string entrypoint = "http")
    {
        ArgumentNullException.ThrowIfNull(target);

        return AddRoute(
            "Http",
            id,
            $"{host}{pathPrefix} to {target.EffectiveResourceId}",
            entrypoint,
            new LoadBalancerRouteMatchValue(Host: host, PathPrefix: pathPrefix),
            new LoadBalancerRouteTargetValue(
                ResourceReference.ReferenceResourceId(
                    target.EffectiveResourceId,
                    target.ResourceTypeId,
                    target.ResourceProviderId),
                EndpointName: endpoint),
            target);
    }

    public LoadBalancerResourceDefinitionBuilder MapPath(
        string host,
        string pathPrefix,
        IResourceDefinitionBuilder target,
        int port,
        string? id = null,
        string entrypoint = "http")
    {
        ArgumentNullException.ThrowIfNull(target);

        return AddRoute(
            "Http",
            id,
            $"{host}{pathPrefix} to {target.EffectiveResourceId}:{port}",
            entrypoint,
            new LoadBalancerRouteMatchValue(Host: host, PathPrefix: pathPrefix),
            new LoadBalancerRouteTargetValue(
                ResourceReference.ReferenceResourceId(
                    target.EffectiveResourceId,
                    target.ResourceTypeId,
                    target.ResourceProviderId),
                Port: port),
            target);
    }

    public LoadBalancerResourceDefinitionBuilder MapTcp(
        int port,
        IResourceDefinitionBuilder target,
        string endpoint = "tcp",
        string? id = null,
        string? entrypoint = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        return AddRoute(
            "Tcp",
            id,
            $"tcp {port} to {target.EffectiveResourceId}",
            entrypoint ?? $"tcp-{port}",
            new LoadBalancerRouteMatchValue(Port: port),
            new LoadBalancerRouteTargetValue(
                ResourceReference.ReferenceResourceId(
                    target.EffectiveResourceId,
                    target.ResourceTypeId,
                    target.ResourceProviderId),
                EndpointName: endpoint),
            target);
    }

    public LoadBalancerResourceDefinitionBuilder MapTcp(
        int port,
        IResourceDefinitionBuilder target,
        int targetPort,
        string? id = null,
        string? entrypoint = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        return AddRoute(
            "Tcp",
            id,
            $"tcp {port} to {target.EffectiveResourceId}:{targetPort}",
            entrypoint ?? $"tcp-{port}",
            new LoadBalancerRouteMatchValue(Port: port),
            new LoadBalancerRouteTargetValue(
                ResourceReference.ReferenceResourceId(
                    target.EffectiveResourceId,
                    target.ResourceTypeId,
                    target.ResourceProviderId),
                Port: targetPort),
            target);
    }

    private LoadBalancerResourceDefinitionBuilder AddEntrypoint(
        string name,
        string protocol,
        int port,
        string exposure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var normalizedName = name.Trim();
        _entrypoints.RemoveAll(entrypoint =>
            string.Equals(entrypoint.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        _entrypoints.Add(new(
            normalizedName,
            protocol,
            port,
            exposure));
        SetObjectAttribute(
            LoadBalancerResourceTypeProvider.Attributes.Entrypoints,
            _entrypoints);
        WithEntrypointCount(_entrypoints.Count);
        return this;
    }

    private LoadBalancerResourceDefinitionBuilder AddRoute(
        string kind,
        string? id,
        string name,
        string entrypoint,
        LoadBalancerRouteMatchValue match,
        LoadBalancerRouteTargetValue target,
        IResourceDefinitionBuilder targetResource)
    {
        ArgumentNullException.ThrowIfNull(targetResource);
        ArgumentException.ThrowIfNullOrWhiteSpace(entrypoint);

        var routeId = string.IsNullOrWhiteSpace(id)
            ? CreateRouteId(kind, match, target)
            : id.Trim();
        _routes.RemoveAll(route =>
            string.Equals(route.Id, routeId, StringComparison.OrdinalIgnoreCase));
        _routes.Add(new(
            routeId,
            name.Trim(),
            kind,
            entrypoint.Trim(),
            match,
            target));
        SetObjectAttribute(
            LoadBalancerResourceTypeProvider.Attributes.Routes,
            _routes);
        WithRouteCount(_routes.Count);
        WithHttpRouteCount(_routes.Count(route =>
            string.Equals(route.Kind, "Http", StringComparison.OrdinalIgnoreCase)));
        WithTcpRouteCount(_routes.Count(route =>
            string.Equals(route.Kind, "Tcp", StringComparison.OrdinalIgnoreCase)));

        return AddBackendTarget(targetResource);
    }

    private string CreateRouteId(
        string kind,
        LoadBalancerRouteMatchValue match,
        LoadBalancerRouteTargetValue target)
    {
        target.Resource.TryGetResourceId(out var targetResourceId);
        var source = string.Equals(kind, "Tcp", StringComparison.OrdinalIgnoreCase)
            ? $"tcp-{match.Port}"
            : string.Join(
                "-",
                new[] { match.Host, match.PathPrefix }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!
                        .Trim()
                        .Replace("/", "-", StringComparison.Ordinal)
                        .Trim('-')));
        var targetPart = target.EndpointName ?? target.Port?.ToString() ?? "target";
        return $"{EffectiveResourceId}:route:{source}:{targetResourceId}:{targetPart}";
    }
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
