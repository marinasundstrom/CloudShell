using System.Text.Json;
using CloudShell.ControlPlane.Providers;

namespace CloudShell.ResourceModel.Tests;

public sealed class ResourceDefinitionDescriptorTests
{
    [Fact]
    public void ResourceTypeDefinition_CanDescribeComplexAttributeShape()
    {
        var typeDefinition = new ResourceTypeDefinition(
            "application.secured-executable",
            ExecutableApplicationResourceTypeProvider.ClassId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["principal"] = new(
                    Required: true,
                    RequiredMessage: "Principal is required.",
                    Description: "Identity information used by the identity capability.",
                    ValueType: ResourceAttributeValueType.ComplexType,
                    ValueShape: new(
                        new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                        {
                            ["subject"] = new(
                                ValueType: ResourceAttributeValueType.String,
                                Required: true),
                            ["claims"] = new(
                                ValueType: ResourceAttributeValueType.ComplexType,
                                ValueShape: new(
                                    new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                                    {
                                        ["type"] = new(
                                            ValueType: ResourceAttributeValueType.String,
                                            Required: true),
                                        ["value"] = new(
                                            ValueType: ResourceAttributeValueType.String,
                                            Required: true)
                                    }),
                                IsCollection: true)
                        }))
            });

        var (attributeName, attribute) = Assert.Single(typeDefinition.Attributes!);
        Assert.Equal("principal", attributeName);
        Assert.Equal(ResourceAttributeValueType.ComplexType, attribute.ValueType);
        Assert.NotNull(attribute.ValueShape);

        var claims = attribute.ValueShape.Attributes!["claims"];
        Assert.Equal(ResourceAttributeValueType.ComplexType, claims.ValueType);
        Assert.True(claims.IsCollection);
        Assert.NotNull(claims.ValueShape);
    }

    [Fact]
    public void ResourceTypeDefinition_CanRoundTripAttributeShapeAsJsonTarget()
    {
        var typeDefinition = new ResourceTypeDefinition(
            "application.secured-executable",
            ExecutableApplicationResourceTypeProvider.ClassId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["principal"] = new(
                    ValueType: ResourceAttributeValueType.ComplexType,
                    ValueShape: new(
                        new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                        {
                            ["subject"] = new(
                                ValueType: ResourceAttributeValueType.String,
                                Required: true)
                        }))
            });

        var json = JsonSerializer.Serialize(typeDefinition, JsonSerializerOptions.Web);
        var roundTrip = JsonSerializer.Deserialize<ResourceTypeDefinition>(json, JsonSerializerOptions.Web);

        Assert.NotNull(roundTrip);
        var (attributeName, attribute) = Assert.Single(roundTrip.Attributes!);
        Assert.Equal("principal", attributeName);
        Assert.Equal(ResourceAttributeValueType.ComplexType, attribute.ValueType);
        Assert.NotNull(attribute.ValueShape);
        var (fieldName, field) = Assert.Single(attribute.ValueShape.Attributes!);
        Assert.Equal("subject", fieldName);
        Assert.Equal(ResourceAttributeValueType.String, field.ValueType);
        Assert.True(field.Required);
    }

    [Fact]
    public void ResourceTypeDefinition_CanDescribeAttributeAuthoringMetadata()
    {
        var typeDefinition = new ResourceTypeDefinition(
            "application.container-app",
            ExecutableApplicationResourceTypeProvider.ClassId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["application.containerImage"] = new(
                    Description: "Container image reference.",
                    ValueType: ResourceAttributeValueType.String,
                    Path: "container.image",
                    DisplayName: "Container image",
                    Aliases:
                    [
                        "container.image",
                        "image"
                    ])
            });

        var (attributeId, attribute) = Assert.Single(typeDefinition.Attributes!);
        Assert.Equal("application.containerImage", attributeId);
        Assert.Equal("container.image", attribute.Path);
        Assert.Equal("Container image", attribute.DisplayName);
        Assert.Equal(["container.image", "image"], attribute.Aliases);
    }

    [Fact]
    public void ResourceTypeDefinition_CanRoundTripAttributeAuthoringMetadataAsJsonTarget()
    {
        var typeDefinition = new ResourceTypeDefinition(
            "application.container-app",
            ExecutableApplicationResourceTypeProvider.ClassId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["application.containerImage"] = new(
                    ValueType: ResourceAttributeValueType.String,
                    Path: "container.image",
                    DisplayName: "Container image",
                    Aliases:
                    [
                        "container.image",
                        "image"
                    ])
            });

        var json = JsonSerializer.Serialize(typeDefinition, JsonSerializerOptions.Web);
        var roundTrip = JsonSerializer.Deserialize<ResourceTypeDefinition>(json, JsonSerializerOptions.Web);

        Assert.NotNull(roundTrip);
        Assert.Contains("\"path\":\"container.image\"", json);
        Assert.Contains("\"displayName\":\"Container image\"", json);
        Assert.Contains("\"aliases\":[\"container.image\",\"image\"]", json);

        var (attributeId, attribute) = Assert.Single(roundTrip.Attributes!);
        Assert.Equal("application.containerImage", attributeId);
        Assert.Equal("container.image", attribute.Path);
        Assert.Equal("Container image", attribute.DisplayName);
        Assert.Equal(["container.image", "image"], attribute.Aliases);
    }

    [Fact]
    public void ResourceTypeDefinition_CanReferenceReusableComplexAttributeShapeAsJsonTarget()
    {
        var typeDefinition = new ResourceTypeDefinition(
            "application.health-checked-executable",
            ExecutableApplicationResourceTypeProvider.ClassId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["runtime:healthChecks"] = ResourceAttributeDefinition.Collection(
                    itemType: ResourceAttributeValueType.ComplexType,
                    itemShapeId: "runtime:healthCheck",
                    description: "Declared health checks evaluated by the runtime provider.",
                    collection: new(MinSize: 1),
                    path: "health.checks",
                    displayName: "Health checks",
                    aliases: ["runtime:healthChecks"])
            },
            AttributeValueShapes: new Dictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>
            {
                ["runtime:healthCheck"] = new(
                    new(
                        new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                        {
                            ["name"] = new(
                                ValueType: ResourceAttributeValueType.String,
                                Required: true),
                            ["path"] = new(ValueType: ResourceAttributeValueType.String),
                            ["intervalSeconds"] = new(ValueType: ResourceAttributeValueType.Integer)
                        }),
                    "Reusable health-check declaration shape.")
            });

        var json = JsonSerializer.Serialize(typeDefinition, JsonSerializerOptions.Web);
        var roundTrip = JsonSerializer.Deserialize<ResourceTypeDefinition>(json, JsonSerializerOptions.Web);

        Assert.NotNull(roundTrip);
        var (_, attribute) = Assert.Single(roundTrip.Attributes!);
        Assert.Equal(ResourceAttributeValueType.ComplexType, attribute.ValueType);
        Assert.Equal("runtime:healthCheck", attribute.ValueShapeId?.ToString());
        Assert.Equal(attribute.ValueShapeId, attribute.ItemShapeId);
        Assert.True(attribute.IsCollection);
        Assert.NotNull(attribute.CollectionOptions);
        Assert.Equal(1, attribute.CollectionOptions.MinSize);
        Assert.Equal("health.checks", attribute.Path);
        Assert.Equal("Health checks", attribute.DisplayName);
        Assert.Equal(["runtime:healthChecks"], attribute.Aliases);
        Assert.DoesNotContain("itemShapeId", json, StringComparison.OrdinalIgnoreCase);
        var (shapeId, shapeDefinition) = Assert.Single(roundTrip.AttributeValueShapes!);
        Assert.Equal("runtime:healthCheck", shapeId);
        var name = shapeDefinition.Shape.Attributes!["name"];
        Assert.True(name.Required);
        Assert.Equal(ResourceAttributeValueType.String, name.ValueType);
    }

    [Fact]
    public void ResourceTypeDefinition_CanDeclareRuntimeProvidedEndpointShapeIdsAsJsonTarget()
    {
        var typeDefinition = new ResourceTypeDefinition(
            "application.endpoint-source",
            ExecutableApplicationResourceTypeProvider.ClassId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["endpoints"] = new(
                    ValueType: ResourceAttributeValueType.ComplexType,
                    ValueShapeId: NetworkingEndpointShapeIds.Endpoint,
                    IsCollection: true),
                ["endpointMappings"] = new(
                    ValueType: ResourceAttributeValueType.ComplexType,
                    ValueShapeId: NetworkingEndpointShapeIds.EndpointMapping,
                    IsCollection: true)
            });

        var json = JsonSerializer.Serialize(typeDefinition, JsonSerializerOptions.Web);
        var roundTrip = JsonSerializer.Deserialize<ResourceTypeDefinition>(json, JsonSerializerOptions.Web);

        Assert.NotNull(roundTrip);
        Assert.Equal(
            NetworkingEndpointShapeIds.Endpoint,
            roundTrip.Attributes!["endpoints"].ValueShapeId);
        Assert.Equal(
            NetworkingEndpointShapeIds.EndpointMapping,
            roundTrip.Attributes!["endpointMappings"].ValueShapeId);
        Assert.Null(roundTrip.AttributeValueShapes);
    }

    [Fact]
    public void ResourceTypeDefinition_CanDeclareResourceReferenceAttributeType()
    {
        var typeDefinition = new ResourceTypeDefinition(
            "application.sql-database",
            "database",
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["database.server"] = new(
                    ValueType: ResourceAttributeValueType.ResourceReference,
                    Required: true)
            });

        var (attributeName, attribute) = Assert.Single(typeDefinition.Attributes!);
        Assert.Equal("database.server", attributeName);
        Assert.Equal(ResourceAttributeValueType.ResourceReference, attribute.ValueType);
        Assert.True(attribute.Required);
    }

    [Fact]
    public void ResourceReferenceAttributeValueMapsConcreteTypeAndSupportsStructuralNavigation()
    {
        var reference = ResourceReference.DependsOnResourceId(
            "application.sql-server:server",
            typeId: SqlServerResourceTypeProvider.ResourceTypeId,
            providerId: SqlServerResourceTypeProvider.ProviderId);
        var json = JsonSerializer.Serialize(reference, JsonSerializerOptions.Web);
        using var document = JsonDocument.Parse(json);

        var attributeValue = ResourceAttributeValue.ResourceReference(reference);
        var mappedReference = attributeValue.ToObject<ResourceReference>();

        foreach (var propertyName in new[] { "value", "relationship", "addressingMode", "typeId", "providerId" })
        {
            Assert.True(
                document.RootElement.TryGetProperty(propertyName, out _),
                $"Expected ResourceReference JSON to include '{propertyName}'.");
            Assert.True(
                attributeValue.ObjectValue?.ContainsKey(propertyName),
                $"Expected ResourceReference attribute value to include '{propertyName}'.");
        }

        Assert.True(attributeValue.TryGetResourceReference(out var navigatedReference));
        var roundTrip = JsonSerializer.Deserialize<ResourceReference>(json, JsonSerializerOptions.Web);

        Assert.Equal(reference, roundTrip);
        Assert.Equal(reference, mappedReference);
        Assert.Equal(reference, navigatedReference);
    }

    [Fact]
    public void ResourceAttributeValueMap_CanSetAndExtractTypedValues()
    {
        var reference = ResourceReference.DependsOnResourceId(
            "application.sql-server:server",
            typeId: SqlServerResourceTypeProvider.ResourceTypeId);
        var endpoint = new NetworkingEndpointMappingValue(
            new NetworkingEndpointReferenceValue(
                ResourceReference.DependsOnResourceId(
                    "cloudshell.network:default",
                    typeId: "cloudshell.network"),
                "public-http"),
            new NetworkingEndpointReferenceValue(reference, "tds"),
            Id: "sql-tds",
            Name: "sql-tds");
        var attributes = new ResourceAttributeValueMap(
            new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                ["database.server"] = ResourceAttributeValue.ResourceReference(reference),
                ["database.endpointMapping"] = ResourceAttributeValue.FromObject(endpoint),
                ["database.enabled"] = true,
                ["database.port"] = 1433
            });

        var mappedReference = attributes.GetObject<ResourceReference>("database.server");
        var mappedEndpoint = attributes.GetObject<NetworkingEndpointMappingValue>("database.endpointMapping");

        Assert.Equal(reference, mappedReference);
        Assert.NotNull(mappedEndpoint);
        Assert.Equal("tds", mappedEndpoint.Target.EndpointName);
        Assert.Equal("true", ResourceAttributeValueMaps.ToScalars(attributes)["database.enabled"]);
        Assert.Equal("1433", ResourceAttributeValueMaps.ToScalars(attributes)["database.port"]);
    }

    [Fact]
    public void ResourceTypeDefinition_SerializesAttributesAsDefinitionMap()
    {
        var typeDefinition = new ResourceTypeDefinition(
            "application.container",
            "container",
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["container.replicas"] = new(
                    DefaultValue: 1,
                    Required: false,
                    ValueType: ResourceAttributeValueType.Integer)
            });

        var json = JsonSerializer.Serialize(typeDefinition, JsonSerializerOptions.Web);

        using var document = JsonDocument.Parse(json);
        var attributes = document.RootElement.GetProperty("attributes");
        var replicas = attributes.GetProperty("container.replicas");
        Assert.Equal(1, replicas.GetProperty("defaultValue").GetInt32());
        Assert.False(replicas.GetProperty("required").GetBoolean());
    }

    [Fact]
    public void ResourceDefinition_SerializesAttributesAsValueMap()
    {
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                ["container.replicas"] = "1"
            });

        var json = JsonSerializer.Serialize(definition, JsonSerializerOptions.Web);

        using var document = JsonDocument.Parse(json);
        var attributes = document.RootElement.GetProperty("attributes");
        Assert.Equal("1", attributes.GetProperty("container.replicas").GetString());
    }

    [Fact]
    public void ResourceDefinition_RoundTripsComplexAttributeValuesAsJsonTarget()
    {
        var endpoint = new NetworkingEndpointValue(
            "http",
            "http",
            8080,
            "Local");
        var definition = new ResourceDefinition(
            "api",
            "application.dotnet-app",
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                ["endpoints"] = ResourceAttributeValue.FromObject(new[] { endpoint })
            });

        var json = JsonSerializer.Serialize(definition, JsonSerializerOptions.Web);
        var roundTrip = JsonSerializer.Deserialize<ResourceDefinition>(json, JsonSerializerOptions.Web);

        Assert.NotNull(roundTrip);
        var endpoints = roundTrip.ResourceAttributeValues["endpoints"];
        Assert.Equal(ResourceAttributeValueKind.Array, endpoints.Kind);
        var projected = Assert.Single(endpoints.ToObject<NetworkingEndpointValue[]>()!);
        Assert.Equal(endpoint, projected);
        Assert.DoesNotContain("\"targetPort\":\"8080\"", json);
    }

    [Fact]
    public void ResourceAttributeValue_ProjectsConcreteClrTypeAndBack()
    {
        var mapping = new NetworkingEndpointMappingValue(
            new NetworkingEndpointReferenceValue(
                ResourceReference.DependsOnResourceId(
                    "cloudshell.network:default",
                    typeId: "cloudshell.network"),
                "public-http"),
            new NetworkingEndpointReferenceValue(
                ResourceReference.DependsOnResourceId(
                    "application.dotnet-app:api",
                    typeId: "application.dotnet-app"),
                "http"));

        var value = ResourceAttributeValue.FromObject(mapping);
        var projected = value.ToObject<NetworkingEndpointMappingValue>();
        var roundTrip = ResourceAttributeValue.FromObject(projected!);

        Assert.NotNull(projected);
        Assert.Equal(mapping.Target.EndpointName, projected.Target.EndpointName);
        Assert.True(projected.Target.Resource.TryGetDependsOnResourceId(out var targetResourceId));
        Assert.Equal("application.dotnet-app:api", targetResourceId);
        Assert.Equal(
            JsonSerializer.Serialize(value, JsonSerializerOptions.Web),
            JsonSerializer.Serialize(roundTrip, JsonSerializerOptions.Web));
    }

    [Fact]
    public void ResourceDefinition_SerializesDependenciesAsResourceReferences()
    {
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    "storage.volume:data",
                    typeId: CloudShellVolumeResourceTypeProvider.ResourceTypeId)
            ]);

        var json = JsonSerializer.Serialize(definition, JsonSerializerOptions.Web);

        using var document = JsonDocument.Parse(json);
        var dependency = Assert.Single(document.RootElement.GetProperty("dependsOn").EnumerateArray());
        Assert.Equal("storage.volume:data", dependency.GetProperty("value").GetString());
        Assert.Equal("dependsOn", dependency.GetProperty("relationship").GetString());
        Assert.Equal("resourceId", dependency.GetProperty("addressingMode").GetString());
        Assert.Equal(
            CloudShellVolumeResourceTypeProvider.ResourceTypeId.ToString(),
            dependency.GetProperty("typeId").GetString());
    }

    [Fact]
    public void ResourceReference_RoundTripsExpectedTypeAndProviderAsJsonTarget()
    {
        var reference = ResourceReference.DependsOnResourceId(
            "application.sql-server:server",
            typeId: SqlServerResourceTypeProvider.ResourceTypeId,
            providerId: SqlServerResourceTypeProvider.ProviderId);

        var json = JsonSerializer.Serialize(reference, JsonSerializerOptions.Web);
        var roundTrip = JsonSerializer.Deserialize<ResourceReference>(json, JsonSerializerOptions.Web);

        Assert.NotNull(roundTrip);
        Assert.Equal(reference.Value, roundTrip.Value);
        Assert.Equal(reference.Relationship, roundTrip.Relationship);
        Assert.Equal(reference.AddressingMode, roundTrip.AddressingMode);
        Assert.Equal(reference.TypeId, roundTrip.TypeId);
        Assert.Equal(reference.ProviderId, roundTrip.ProviderId);
    }

    [Fact]
    public void ResourceReference_DistinguishesResourceIdAddressingFromDependsOnSemantics()
    {
        var reference = ResourceReference.BelongsToResourceId(
            "application.sql-server:server",
            SqlServerResourceTypeProvider.ResourceTypeId);

        Assert.True(reference.TryGetResourceId(out var resourceId));
        Assert.False(reference.TryGetDependsOnResourceId(out _));
        Assert.Equal("application.sql-server:server", resourceId);
    }

}
