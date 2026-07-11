using CloudShell.ControlPlane.Providers;
using CloudShell.ResourceModel;

namespace CloudShell.Sample.Tests;

public sealed class ProjectEnvironmentVariableReaderTests
{
    [Fact]
    public void AspNetCoreProjectRuntimeReadsFlattenedLiteralEnvironmentVariables()
    {
        var resource = CreateAspNetCoreProjectResource(
            [
                new(
                    "project.hotReload",
                    false,
                    ResourceDefinitionValueSource.ResourceState),
                new(
                    "project.environmentVariables.CLOUDSHELL_METRIC_INGEST_ENDPOINT.value",
                    "http://127.0.0.1:5011/api/control-plane/v1/metrics/ingest",
                    ResourceDefinitionValueSource.ResourceState)
            ]);

        var startInfo = new AspNetCoreProjectProcessCommandFactory()
            .CreateStartInfo(resource, "/tmp/test-api.csproj");

        Assert.Equal(
            "http://127.0.0.1:5011/api/control-plane/v1/metrics/ingest",
            startInfo.Environment["CLOUDSHELL_METRIC_INGEST_ENDPOINT"]);
    }

    [Fact]
    public void AspNetCoreProjectRuntimeReadsObjectLiteralNumericEnvironmentVariableAsString()
    {
        var resource = CreateAspNetCoreProjectResource(
        [
            new(
                "project.hotReload",
                false,
                ResourceDefinitionValueSource.ResourceState),
            new(
                AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables,
                ResourceAttributeValue.Object(
                    new Dictionary<string, ResourceAttributeValue>
                    {
                        ["RabbitMQ__Port"] = ResourceAttributeValue.Object(
                            new Dictionary<string, ResourceAttributeValue>
                            {
                                ["value"] = ResourceAttributeValue.Integer(5678)
                            })
                    }),
                ResourceDefinitionValueSource.ResourceState)
        ]);

        var startInfo = new AspNetCoreProjectProcessCommandFactory()
            .CreateStartInfo(resource, "/tmp/test-api.csproj");

        Assert.Equal("5678", startInfo.Environment["RabbitMQ__Port"]);
    }

    [Fact]
    public void JavaAppRuntimeReadsObjectLiteralNumericEnvironmentVariableAsString()
    {
        var resource = CreateJavaAppResource(
        [
            new(
                JavaAppResourceTypeProvider.Attributes.EnvironmentVariables,
                ResourceAttributeValue.Object(
                    new Dictionary<string, ResourceAttributeValue>
                    {
                        ["PORT"] = ResourceAttributeValue.Object(
                            new Dictionary<string, ResourceAttributeValue>
                            {
                                ["value"] = ResourceAttributeValue.Integer(5282)
                            })
                    }),
                ResourceDefinitionValueSource.ResourceState)
        ]);

        var startInfo = new JavaAppProcessCommandFactory()
            .CreateStartInfo(resource, "/tmp");

        Assert.Equal("5282", startInfo.Environment["PORT"]);
    }

    private static Resource CreateAspNetCoreProjectResource(
        IReadOnlyList<ResourceAttributeResolution> attributes)
    {
        var provider = new AspNetCoreProjectResourceTypeProvider();
        return CreateResource(
            "test-api",
            "application.dotnet-app:test-api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            AspNetCoreProjectResourceTypeProvider.ClassDefinition,
            provider.TypeDefinition,
            attributes);
    }

    private static Resource CreateJavaAppResource(
        IReadOnlyList<ResourceAttributeResolution> attributes)
    {
        var provider = new JavaAppResourceTypeProvider();
        return CreateResource(
            "test-java",
            "application.java-app:test-java",
            JavaAppResourceTypeProvider.ResourceTypeId,
            JavaAppResourceTypeProvider.ClassDefinition,
            provider.TypeDefinition,
            attributes);
    }

    private static Resource CreateResource(
        string name,
        string resourceId,
        ResourceTypeId resourceTypeId,
        ResourceClassDefinition classDefinition,
        ResourceTypeDefinition typeDefinition,
        IReadOnlyList<ResourceAttributeResolution> attributes)
    {
        var emptyAttributes = new ResourceAttributeSet([]);
        var emptyCapabilities = new ResourceCapabilitySet([]);
        var emptyOperations = new ResourceOperationSet([]);
        var resourceClass = new ResourceClass(
            classDefinition,
            emptyAttributes,
            emptyCapabilities,
            emptyOperations);

        return new Resource(
            new ResourceState(
                name,
                resourceTypeId,
                ResourceId: resourceId),
            resourceClass,
            new ResourceType(
                typeDefinition,
                resourceClass,
                emptyAttributes,
                emptyCapabilities,
                emptyOperations),
            new ResourceAttributeSet(attributes),
            emptyCapabilities,
            emptyOperations,
            []);
    }
}
