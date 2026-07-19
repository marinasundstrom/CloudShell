using System.Text.Json;

namespace CloudShell.ResourceModel.Tests;

public sealed class ResourceDefinitionSchemaValidatorTests
{
    [Fact]
    public void Validate_CanonicalizesAuthoredAttributePaths()
    {
        var validator = CreateValidator(
            new ResourceTypeDefinition(
                ResourceTypeId.Create("sample.type"),
                ResourceClassId.Create("sample.class"),
                Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                {
                    [ResourceAttributeId.Create("sample.image")] = new(
                        ValueType: ResourceAttributeValueType.String,
                        Path: "image")
                }),
            new ResourceClassDefinition(ResourceClassId.Create("sample.class")));
        var definition = new ResourceDefinition(
            "api",
            ResourceTypeId.Create("sample.type"),
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ResourceAttributeId.Create("image")] = "api:latest"
            });

        var result = validator.Validate(definition);

        Assert.False(result.HasErrors);
        Assert.True(result.Definition.ResourceAttributeValues.ContainsKey(
            ResourceAttributeId.Create("sample.image")));
    }

    [Fact]
    public void Validate_ReportsUnknownAttributes()
    {
        var validator = CreateValidator(
            new ResourceTypeDefinition(
                ResourceTypeId.Create("sample.type"),
                ResourceClassId.Create("sample.class")),
            new ResourceClassDefinition(ResourceClassId.Create("sample.class")));
        var definition = new ResourceDefinition(
            "api",
            ResourceTypeId.Create("sample.type"),
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ResourceAttributeId.Create("sample.unknown")] = "value"
            });

        var result = validator.Validate(definition);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.UnknownAttribute, diagnostic.Code);
        Assert.Equal("sample.unknown", diagnostic.Target);
    }

    [Fact]
    public void Validate_AllowsAttributesFromClassOrTypeDeclaredCapabilities()
    {
        var capabilityId = ResourceCapabilityId.Create("sample.environment");
        var attributeId = ResourceAttributeId.Create("sample.environment.variables");
        var validator = CreateValidator(
            new ResourceTypeDefinition(
                ResourceTypeId.Create("sample.type"),
                ResourceClassId.Create("sample.class")),
            new ResourceClassDefinition(
                ResourceClassId.Create("sample.class"),
                Capabilities: [new(capabilityId)]),
            new ResourceCapabilityAttributeSchema(
                capabilityId,
                new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                {
                    [attributeId] = new(
                        ValueType: ResourceAttributeValueType.ComplexType,
                        Path: "environmentVariables")
                }));
        var definition = new ResourceDefinition(
            "api",
            ResourceTypeId.Create("sample.type"),
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [attributeId] = ResourceAttributeValue.Object(
                    new Dictionary<string, ResourceAttributeValue>
                    {
                        ["ASPNETCORE_ENVIRONMENT"] = "Development"
                    })
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [capabilityId] = ResourceDefinitionJson.EmptyObject
            });

        var result = validator.Validate(definition);

        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Validate_ReportsResourceDeclaredCapabilitiesThatAreNotInSchema()
    {
        var capabilityId = ResourceCapabilityId.Create("sample.environment");
        var attributeId = ResourceAttributeId.Create("sample.environment.variables");
        var validator = CreateValidator(
            new ResourceTypeDefinition(
                ResourceTypeId.Create("sample.type"),
                ResourceClassId.Create("sample.class")),
            new ResourceClassDefinition(ResourceClassId.Create("sample.class")),
            new ResourceCapabilityAttributeSchema(
                capabilityId,
                new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                {
                    [attributeId] = new(
                        ValueType: ResourceAttributeValueType.ComplexType,
                        Path: "environmentVariables")
                }));
        var definition = new ResourceDefinition(
            "api",
            ResourceTypeId.Create("sample.type"),
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [attributeId] = ResourceAttributeValue.Object(
                    new Dictionary<string, ResourceAttributeValue>
                    {
                        ["ASPNETCORE_ENVIRONMENT"] = "Development"
                    })
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [capabilityId] = ResourceDefinitionJson.EmptyObject
            });

        var result = validator.Validate(definition);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == ResourceDefinitionDiagnosticCodes.UnknownCapability &&
                diagnostic.Target == capabilityId.ToString());
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == ResourceDefinitionDiagnosticCodes.UnknownAttribute &&
                diagnostic.Target == attributeId.ToString());
    }

    [Fact]
    public void Validate_UsesDefinitionSchemaForRequiredReadOnlyAndValueTypeDiagnostics()
    {
        var validator = CreateValidator(
            new ResourceTypeDefinition(
                ResourceTypeId.Create("sample.type"),
                ResourceClassId.Create("sample.class"),
                Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                {
                    [ResourceAttributeId.Create("sample.name")] = new(
                        Required: true,
                        RequiredMessage: "Name is required.",
                        ValueType: ResourceAttributeValueType.String),
                    [ResourceAttributeId.Create("sample.observed")] = new(
                        ValueType: ResourceAttributeValueType.Integer,
                        ReadOnly: true)
                }),
            new ResourceClassDefinition(ResourceClassId.Create("sample.class")));
        var definition = new ResourceDefinition(
            "api",
            ResourceTypeId.Create("sample.type"),
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ResourceAttributeId.Create("sample.observed")] = "not-a-number"
            });

        var result = validator.Validate(definition);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == ResourceDefinitionDiagnosticCodes.RequiredAttributeMissing &&
                diagnostic.Target == "sample.name");
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == ResourceDefinitionDiagnosticCodes.ReadOnlyAttributeChange &&
                diagnostic.Target == "sample.observed");
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == ResourceDefinitionDiagnosticCodes.AttributeValueInvalid &&
                diagnostic.Target == "sample.observed");
    }

    private static ResourceDefinitionSchemaValidator CreateValidator(
        ResourceTypeDefinition typeDefinition,
        ResourceClassDefinition classDefinition,
        params IResourceCapabilityAttributeProvider[] capabilityAttributeProviders) =>
        new(new ResourceDefinitionSchemaCatalog(
            [typeDefinition],
            capabilityAttributeProviders,
            [classDefinition]));
}
