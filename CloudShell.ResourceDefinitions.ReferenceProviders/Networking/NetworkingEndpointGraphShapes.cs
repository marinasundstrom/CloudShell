namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public static class NetworkingEndpointShapeIds
{
    public static readonly ResourceAttributeValueShapeId Endpoint = "networking.endpoint";
    public static readonly ResourceAttributeValueShapeId EndpointRequest = "networking.endpointRequest";
    public static readonly ResourceAttributeValueShapeId EndpointReference = "networking.endpointReference";
    public static readonly ResourceAttributeValueShapeId EndpointNetworkMapping = "networking.endpointNetworkMapping";
    public static readonly ResourceAttributeValueShapeId EndpointMapping = "networking.endpointMapping";
}

public sealed class NetworkingEndpointShapeProvider : IResourceAttributeValueShapeProvider
{
    public IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition> GetAttributeValueShapes() =>
        NetworkingEndpointShapes.All;
}

public static class NetworkingEndpointShapes
{
    public static ResourceAttributeValueShapeDefinition Endpoint { get; } = new(
        new(
            new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["name"] = RequiredString("Endpoint name is required."),
                ["protocol"] = RequiredString("Endpoint protocol is required."),
                ["targetPort"] = new(ValueType: ResourceAttributeValueType.Integer),
                ["exposure"] = new(ValueType: ResourceAttributeValueType.String)
            }),
        "Runtime endpoint contract shape.");

    public static ResourceAttributeValueShapeDefinition EndpointRequest { get; } = new(
        new(
            new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["name"] = RequiredString("Endpoint request name is required."),
                ["protocol"] = RequiredString("Endpoint request protocol is required."),
                ["targetPort"] = new(ValueType: ResourceAttributeValueType.Integer),
                ["host"] = new(ValueType: ResourceAttributeValueType.String),
                ["port"] = new(ValueType: ResourceAttributeValueType.Integer),
                ["ipAddress"] = new(ValueType: ResourceAttributeValueType.String),
                ["exposure"] = new(ValueType: ResourceAttributeValueType.String),
                ["assignment"] = new(ValueType: ResourceAttributeValueType.String),
                ["network"] = new(ValueType: ResourceAttributeValueType.ResourceReference),
                ["providerEndpointId"] = new(ValueType: ResourceAttributeValueType.String)
            }),
        "Runtime endpoint assignment request shape.");

    public static ResourceAttributeValueShapeDefinition EndpointReference { get; } = new(
        new(
            new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["resource"] = new(
                    Required: true,
                    RequiredMessage: "Endpoint resource reference is required.",
                    ValueType: ResourceAttributeValueType.ResourceReference),
                ["endpointName"] = RequiredString("Endpoint name is required.")
            }),
        "Runtime endpoint reference shape.");

    public static ResourceAttributeValueShapeDefinition EndpointNetworkMapping { get; } = new(
        new(
            new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["id"] = RequiredString("Endpoint network mapping id is required."),
                ["name"] = RequiredString("Endpoint network mapping name is required."),
                ["target"] = EndpointReferenceAttribute("Endpoint network mapping target is required."),
                ["address"] = RequiredString("Endpoint network mapping address is required."),
                ["exposure"] = new(ValueType: ResourceAttributeValueType.String),
                ["network"] = new(ValueType: ResourceAttributeValueType.ResourceReference),
                ["provider"] = new(ValueType: ResourceAttributeValueType.ResourceReference),
                ["sourceEndpointName"] = new(ValueType: ResourceAttributeValueType.String)
            }),
        "Runtime topology-specific endpoint address shape.");

    public static ResourceAttributeValueShapeDefinition EndpointMapping { get; } = new(
        new(
            new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["id"] = new(ValueType: ResourceAttributeValueType.String),
                ["name"] = new(ValueType: ResourceAttributeValueType.String),
                ["source"] = EndpointReferenceAttribute("Endpoint mapping source is required."),
                ["target"] = EndpointReferenceAttribute("Endpoint mapping target is required."),
                ["network"] = new(ValueType: ResourceAttributeValueType.ResourceReference),
                ["provider"] = new(ValueType: ResourceAttributeValueType.ResourceReference)
            }),
        "Runtime source-to-target endpoint mapping shape.");

    public static IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition> All { get; } =
        new Dictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>
        {
            [NetworkingEndpointShapeIds.Endpoint] = Endpoint,
            [NetworkingEndpointShapeIds.EndpointRequest] = EndpointRequest,
            [NetworkingEndpointShapeIds.EndpointReference] = EndpointReference,
            [NetworkingEndpointShapeIds.EndpointNetworkMapping] = EndpointNetworkMapping,
            [NetworkingEndpointShapeIds.EndpointMapping] = EndpointMapping
        };

    private static ResourceAttributeDefinition RequiredString(string message) =>
        new(
            Required: true,
            RequiredMessage: message,
            ValueType: ResourceAttributeValueType.String);

    private static ResourceAttributeDefinition EndpointReferenceAttribute(string message) =>
        new(
            Required: true,
            RequiredMessage: message,
            ValueType: ResourceAttributeValueType.ComplexType,
            ValueShapeId: NetworkingEndpointShapeIds.EndpointReference);
}

public sealed record NetworkingEndpointValue(
    string Name,
    string Protocol,
    int? TargetPort = null,
    string? Exposure = null);

public sealed record NetworkingEndpointRequestValue(
    string Name,
    string Protocol,
    int? TargetPort = null,
    string? Host = null,
    int? Port = null,
    string? IpAddress = null,
    string? Exposure = null,
    string? Assignment = null,
    ResourceReference? Network = null,
    string? ProviderEndpointId = null);

public sealed record NetworkingEndpointReferenceValue(
    ResourceReference Resource,
    string EndpointName);

public sealed record NetworkingEndpointNetworkMappingValue(
    string Id,
    string Name,
    NetworkingEndpointReferenceValue Target,
    string Address,
    string? Exposure = null,
    ResourceReference? Network = null,
    ResourceReference? Provider = null,
    string? SourceEndpointName = null);

public sealed record NetworkingEndpointMappingValue(
    NetworkingEndpointReferenceValue Source,
    NetworkingEndpointReferenceValue Target,
    string? Id = null,
    string? Name = null,
    ResourceReference? Network = null,
    ResourceReference? Provider = null);
