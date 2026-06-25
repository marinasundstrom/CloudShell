using System.Text.Json;
using CloudShell.ResourceDefinitions.ReferenceProviders;

namespace CloudShell.ResourceDefinitions.Tests;

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
    public void ResourceTypeDefinition_CanReferenceReusableComplexAttributeShapeAsJsonTarget()
    {
        var typeDefinition = new ResourceTypeDefinition(
            "application.health-checked-executable",
            ExecutableApplicationResourceTypeProvider.ClassId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["runtime:healthChecks"] = new(
                    Description: "Declared health checks evaluated by the runtime provider.",
                    ValueType: ResourceAttributeValueType.ComplexType,
                    ValueShapeId: "runtime:healthCheck",
                    IsCollection: true,
                    Collection: new(MinSize: 1))
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
        Assert.True(attribute.IsCollection);
        Assert.NotNull(attribute.Collection);
        Assert.Equal(1, attribute.Collection.MinSize);
        var (shapeId, shapeDefinition) = Assert.Single(roundTrip.AttributeValueShapes!);
        Assert.Equal("runtime:healthCheck", shapeId);
        var name = shapeDefinition.Shape.Attributes!["name"];
        Assert.True(name.Required);
        Assert.Equal(ResourceAttributeValueType.String, name.ValueType);
    }

    [Fact]
    public void ResourceTypeDefinition_CanDeclareResourceReferenceAttributeShape()
    {
        var typeDefinition = new ResourceTypeDefinition(
            "application.sql-database",
            "database",
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["database.server"] = new(
                    ValueType: ResourceAttributeValueType.ComplexType,
                    ValueShapeId: ResourceReference.AttributeValueShapeId,
                    Required: true)
            },
            AttributeValueShapes: new Dictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>
            {
                [ResourceReference.AttributeValueShapeId] =
                    ResourceReference.CreateAttributeValueShapeDefinition()
            });

        var (attributeName, attribute) = Assert.Single(typeDefinition.Attributes!);
        Assert.Equal("database.server", attributeName);
        Assert.Equal(ResourceAttributeValueType.ComplexType, attribute.ValueType);
        Assert.Equal(ResourceReference.AttributeValueShapeId, attribute.ValueShapeId);
        Assert.True(attribute.Required);

        var (shapeId, shapeDefinition) = Assert.Single(typeDefinition.AttributeValueShapes!);
        Assert.Equal(ResourceReference.AttributeValueShapeId, shapeId);
        Assert.Equal(
            ["value", "relationship", "addressingMode", "typeId", "providerId"],
            shapeDefinition.Shape.Attributes!.Keys.Select(key => key.ToString()).ToArray());
        Assert.True(shapeDefinition.Shape.Attributes["value"].Required);
        Assert.True(shapeDefinition.Shape.Attributes["relationship"].Required);
        Assert.True(shapeDefinition.Shape.Attributes["addressingMode"].Required);
    }

    [Fact]
    public void ResourceReferenceAttributeShapeMatchesJsonTarget()
    {
        var reference = ResourceReference.ResourceId(
            "application.sql-server:server",
            typeId: SqlServerResourceTypeProvider.ResourceTypeId,
            providerId: SqlServerResourceTypeProvider.ProviderId);
        var json = JsonSerializer.Serialize(reference, JsonSerializerOptions.Web);
        using var document = JsonDocument.Parse(json);

        var shape = ResourceReference.CreateAttributeValueShapeDefinition().Shape;
        var attributeValue = ResourceAttributeValue.FromObject(reference);
        var mappedReference = attributeValue.ToObject<ResourceReference>();

        foreach (var attributeId in shape.Attributes!.Keys)
        {
            Assert.True(
                document.RootElement.TryGetProperty(attributeId.ToString(), out _),
                $"Expected ResourceReference JSON to include '{attributeId}'.");
            Assert.True(
                attributeValue.ObjectValue?.ContainsKey(attributeId.ToString()),
                $"Expected ResourceReference attribute value to include '{attributeId}'.");
        }

        var roundTrip = JsonSerializer.Deserialize<ResourceReference>(json, JsonSerializerOptions.Web);

        Assert.Equal(reference, roundTrip);
        Assert.Equal(reference, mappedReference);
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
    public void ResourceDefinition_SerializesDependenciesAsResourceReferences()
    {
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            DependsOn:
            [
                ResourceReference.ResourceId(
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
        var reference = ResourceReference.ResourceId(
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
}
