namespace CloudShell.ResourceModel.Tests;

public sealed class ResourceTemplateSerializerTests
{
    [Fact]
    public void DeserializeTemplate_YamlUsesResourceDefinitionTypeAliasAndDefaultName()
    {
        const string yaml = """
resources:
  - type: application.container-app
    name: api
    displayName: API
    attributes:
      container.image: api:latest
      container.replicas: 3
metadata:
  source: test
""";

        var template = ResourceTemplateSerializer.DeserializeTemplate(yaml);

        Assert.Equal("local", template.Name);
        Assert.Equal("test", template.Metadata?["source"]);

        var resource = Assert.Single(template.Resources);
        Assert.Equal("api", resource.Name);
        Assert.Equal(ResourceTypeId.Create("application.container-app"), resource.TypeId);
        Assert.Equal("API", resource.DisplayName);
        Assert.True(resource.ResourceAttributeValues.TryGetValue(
            ResourceAttributeId.Create("container.image"),
            out var image));
        Assert.Equal("api:latest", image.StringValue);
        Assert.True(resource.ResourceAttributeValues.TryGetValue(
            ResourceAttributeId.Create("container.replicas"),
            out var replicas));
        Assert.Equal(3, replicas.IntegerValue);
    }

    [Fact]
    public void SerializeAndDeserializeDefinition_YamlUsesTypeAliasWithoutRenamingReferences()
    {
        var definition = new ResourceDefinition(
            "api",
            ResourceTypeId.Create("application.container-app"),
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    "application.database:db",
                    ResourceTypeId.Create("application.database"))
            ],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ResourceAttributeId.Create("container.image")] = "api:latest"
            });

        var yaml = ResourceTemplateSerializer.SerializeDefinition(definition);
        var roundTripped = ResourceTemplateSerializer.DeserializeDefinition(yaml);

        Assert.Contains("type: application.container-app", yaml);
        Assert.DoesNotContain("typeId: application.container-app", yaml);
        Assert.Contains("typeId: application.database", yaml);
        Assert.Equal(definition.Name, roundTripped.Name);
        Assert.Equal(definition.TypeId, roundTripped.TypeId);

        var dependency = Assert.Single(roundTripped.StartupDependencies);
        Assert.Equal(ResourceTypeId.Create("application.database"), dependency.TypeId);
    }

    [Fact]
    public void DeserializeTemplate_JsonKeepsContractTypeId()
    {
        const string json = """
{
  "name": "local-app",
  "resources": [
    {
      "name": "api",
      "typeId": "application.container-app"
    }
  ]
}
""";

        var template = ResourceTemplateSerializer.DeserializeTemplate(
            json,
            ResourceTemplateFormat.Json);
        var serialized = ResourceTemplateSerializer.SerializeTemplate(
            template,
            ResourceTemplateFormat.Json);

        var resource = Assert.Single(template.Resources);
        Assert.Equal("local-app", template.Name);
        Assert.Equal(ResourceTypeId.Create("application.container-app"), resource.TypeId);
        Assert.Contains("\"typeId\": \"application.container-app\"", serialized);
    }
}
