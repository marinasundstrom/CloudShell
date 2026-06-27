namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public static class LoadBalancerShapeIds
{
    public static readonly ResourceAttributeValueShapeId Entrypoint =
        "loadBalancer.entrypoint";

    public static readonly ResourceAttributeValueShapeId Route =
        "loadBalancer.route";

    public static readonly ResourceAttributeValueShapeId RouteMatch =
        "loadBalancer.route.match";

    public static readonly ResourceAttributeValueShapeId RouteTarget =
        "loadBalancer.route.target";
}

public sealed class LoadBalancerShapeProvider : IResourceAttributeValueShapeProvider
{
    public IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition> GetAttributeValueShapes() =>
        LoadBalancerShapes.All;
}

public static class LoadBalancerShapes
{
    public static ResourceAttributeValueShapeDefinition Entrypoint { get; } = new(
        new(
            new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["name"] = RequiredString("Load balancer entrypoint name is required."),
                ["protocol"] = RequiredString("Load balancer entrypoint protocol is required."),
                ["port"] = new(
                    Required: true,
                    RequiredMessage: "Load balancer entrypoint port is required.",
                    ValueType: ResourceAttributeValueType.Integer),
                ["exposure"] = new(ValueType: ResourceAttributeValueType.String)
            }),
        "Load balancer frontend entrypoint shape.");

    public static ResourceAttributeValueShapeDefinition RouteMatch { get; } = new(
        new(
            new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["host"] = new(ValueType: ResourceAttributeValueType.String),
                ["pathPrefix"] = new(ValueType: ResourceAttributeValueType.String),
                ["port"] = new(ValueType: ResourceAttributeValueType.Integer)
            }),
        "Load balancer route match shape.");

    public static ResourceAttributeValueShapeDefinition RouteTarget { get; } = new(
        new(
            new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["resource"] = new(
                    Required: true,
                    RequiredMessage: "Load balancer route target resource is required.",
                    ValueType: ResourceAttributeValueType.ResourceReference),
                ["endpointName"] = new(ValueType: ResourceAttributeValueType.String),
                ["port"] = new(ValueType: ResourceAttributeValueType.Integer)
            }),
        "Load balancer route target shape.");

    public static ResourceAttributeValueShapeDefinition Route { get; } = new(
        new(
            new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["id"] = RequiredString("Load balancer route id is required."),
                ["name"] = RequiredString("Load balancer route name is required."),
                ["kind"] = RequiredString("Load balancer route kind is required."),
                ["entrypointName"] = RequiredString("Load balancer route entrypoint is required."),
                ["match"] = new(
                    Required: true,
                    RequiredMessage: "Load balancer route match is required.",
                    ValueType: ResourceAttributeValueType.ComplexType,
                    ValueShapeId: LoadBalancerShapeIds.RouteMatch),
                ["target"] = new(
                    Required: true,
                    RequiredMessage: "Load balancer route target is required.",
                    ValueType: ResourceAttributeValueType.ComplexType,
                    ValueShapeId: LoadBalancerShapeIds.RouteTarget)
            }),
        "Load balancer route shape.");

    public static IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition> All { get; } =
        new Dictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>
        {
            [LoadBalancerShapeIds.Entrypoint] = Entrypoint,
            [LoadBalancerShapeIds.Route] = Route,
            [LoadBalancerShapeIds.RouteMatch] = RouteMatch,
            [LoadBalancerShapeIds.RouteTarget] = RouteTarget
        };

    private static ResourceAttributeDefinition RequiredString(string message) =>
        new(
            Required: true,
            RequiredMessage: message,
            ValueType: ResourceAttributeValueType.String);
}

public sealed record LoadBalancerEntrypointValue(
    string Name,
    string Protocol,
    int Port,
    string? Exposure = null);

public sealed record LoadBalancerRouteValue(
    string Id,
    string Name,
    string Kind,
    string EntrypointName,
    LoadBalancerRouteMatchValue Match,
    LoadBalancerRouteTargetValue Target);

public sealed record LoadBalancerRouteMatchValue(
    string? Host = null,
    string? PathPrefix = null,
    int? Port = null);

public sealed record LoadBalancerRouteTargetValue(
    ResourceReference Resource,
    string? EndpointName = null,
    int? Port = null);
