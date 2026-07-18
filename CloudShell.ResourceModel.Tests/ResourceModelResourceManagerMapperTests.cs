using System.Text.Json;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;

namespace CloudShell.ResourceModel.Tests;

public sealed class ResourceModelResourceManagerMapperTests
{
    [Fact]
    public void ToResourceManagerResource_PreservesComplexAttributesAsJson()
    {
        var typeProvider = new AspNetCoreProjectResourceTypeProvider();
        var resolver = new ResourceResolver(
            [AspNetCoreProjectResourceTypeProvider.ClassDefinition],
            [typeProvider.TypeDefinition]);
        var definition = new ResourceDefinition(
            "api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] = "./Api/Api.csproj",
                [EnvironmentVariablesCapabilityProvider.AttributeId] =
                    ResourceAttributeValue.FromObject(new Dictionary<string, ResourceEnvironmentVariableValue>
                    {
                        ["SERVICE_APIKEY"] = new(Value: "local-secret")
                    })
            });

        var resource = resolver.Resolve(definition);
        var projected = ResourceModelResourceManagerMapper.ToResourceManagerResource(resource);

        Assert.True(projected.ResourceAttributes.TryGetValue(
            EnvironmentVariablesCapabilityProvider.AttributeId.ToString(),
            out var serialized));
        using var document = JsonDocument.Parse(serialized);
        Assert.True(document.RootElement.TryGetProperty("SERVICE_APIKEY", out var variable));
        Assert.Equal("local-secret", variable.GetProperty("value").GetString());
    }
}
