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
                    ValueShape: new(
                        ResourceAttributeValueKind.Object,
                        Fields:
                        [
                            new(
                                "subject",
                                new(ResourceAttributeValueKind.String),
                                Required: true),
                            new(
                                "claims",
                                new(
                                    ResourceAttributeValueKind.Array,
                                    ElementShape: new(
                                        ResourceAttributeValueKind.Object,
                                        Fields:
                                        [
                                            new(
                                                "type",
                                                new(ResourceAttributeValueKind.String),
                                                Required: true),
                                            new(
                                                "value",
                                                new(ResourceAttributeValueKind.String),
                                                Required: true)
                                        ])))
                        ]))
            });

        var (attributeName, attribute) = Assert.Single(typeDefinition.Attributes!);
        Assert.Equal("principal", attributeName);
        Assert.NotNull(attribute.ValueShape);
        Assert.Equal(ResourceAttributeValueKind.Object, attribute.ValueShape.Kind);

        var claims = Assert.Single(attribute.ValueShape.Fields!, field => field.Name == "claims");
        Assert.Equal(ResourceAttributeValueKind.Array, claims.ValueShape.Kind);
        Assert.Equal(ResourceAttributeValueKind.Object, claims.ValueShape.ElementShape?.Kind);
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
                    ValueShape: new(
                        ResourceAttributeValueKind.Object,
                        Fields:
                        [
                            new("subject", new(ResourceAttributeValueKind.String), Required: true)
                        ]))
            });

        var json = JsonSerializer.Serialize(typeDefinition, JsonSerializerOptions.Web);
        var roundTrip = JsonSerializer.Deserialize<ResourceTypeDefinition>(json, JsonSerializerOptions.Web);

        Assert.NotNull(roundTrip);
        var (attributeName, attribute) = Assert.Single(roundTrip.Attributes!);
        Assert.Equal("principal", attributeName);
        Assert.NotNull(attribute.ValueShape);
        Assert.Equal(ResourceAttributeValueKind.Object, attribute.ValueShape.Kind);
        var field = Assert.Single(attribute.ValueShape.Fields!);
        Assert.Equal("subject", field.Name);
        Assert.Equal(ResourceAttributeValueKind.String, field.ValueShape.Kind);
        Assert.True(field.Required);
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
                    ValueShape: new(ResourceAttributeValueKind.Integer))
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
                ResourceReference.ResourceId("storage.volume:data")
            ]);

        var json = JsonSerializer.Serialize(definition, JsonSerializerOptions.Web);

        using var document = JsonDocument.Parse(json);
        var dependency = Assert.Single(document.RootElement.GetProperty("dependsOn").EnumerateArray());
        Assert.Equal("storage.volume:data", dependency.GetProperty("value").GetString());
        Assert.Equal("dependsOn", dependency.GetProperty("relationship").GetString());
        Assert.Equal("resourceId", dependency.GetProperty("addressingMode").GetString());
    }
}
