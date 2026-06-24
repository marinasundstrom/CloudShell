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
            AttributeDefinitions:
            [
                new(
                    "principal",
                    IsRequired: true,
                    RequiredMessage: "Principal is required.",
                    Description: "Identity information used by the identity capability.",
                    ValueShape: new(
                        ResourceAttributeValueKind.Object,
                        Fields:
                        [
                            new(
                                "subject",
                                new(ResourceAttributeValueKind.String),
                                IsRequired: true),
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
                                                IsRequired: true),
                                            new(
                                                "value",
                                                new(ResourceAttributeValueKind.String),
                                                IsRequired: true)
                                        ])))
                        ]))
            ]);

        var attribute = Assert.Single(typeDefinition.AttributeDefinitions!);
        Assert.Equal("principal", attribute.Name);
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
            AttributeDefinitions:
            [
                new(
                    "principal",
                    ValueShape: new(
                        ResourceAttributeValueKind.Object,
                        Fields:
                        [
                            new("subject", new(ResourceAttributeValueKind.String), IsRequired: true)
                        ]))
            ]);

        var json = JsonSerializer.Serialize(typeDefinition);
        var roundTrip = JsonSerializer.Deserialize<ResourceTypeDefinition>(json);

        Assert.NotNull(roundTrip);
        var attribute = Assert.Single(roundTrip.AttributeDefinitions!);
        Assert.Equal("principal", attribute.Name);
        Assert.NotNull(attribute.ValueShape);
        Assert.Equal(ResourceAttributeValueKind.Object, attribute.ValueShape.Kind);
        var field = Assert.Single(attribute.ValueShape.Fields!);
        Assert.Equal("subject", field.Name);
        Assert.Equal(ResourceAttributeValueKind.String, field.ValueShape.Kind);
        Assert.True(field.IsRequired);
    }
}
