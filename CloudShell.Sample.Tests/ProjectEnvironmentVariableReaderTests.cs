using CloudShell.ControlPlane.Providers;
using CloudShell.ResourceModel;

namespace CloudShell.Sample.Tests;

public sealed class ProjectEnvironmentVariableReaderTests
{
    [Fact]
    public void AspNetCoreProjectRuntimeReadsFlattenedLiteralEnvironmentVariables()
    {
        var provider = new AspNetCoreProjectResourceTypeProvider();
        var emptyAttributes = new ResourceAttributeSet([]);
        var emptyCapabilities = new ResourceCapabilitySet([]);
        var emptyOperations = new ResourceOperationSet([]);
        var resource = new Resource(
            new ResourceState(
                "test-api",
                AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
                ResourceId: "application.aspnet-core-project:test-api"),
            new ResourceClass(
                AspNetCoreProjectResourceTypeProvider.ClassDefinition,
                emptyAttributes,
                emptyCapabilities,
                emptyOperations),
            new ResourceType(
                provider.TypeDefinition,
                new ResourceClass(
                    AspNetCoreProjectResourceTypeProvider.ClassDefinition,
                    emptyAttributes,
                    emptyCapabilities,
                    emptyOperations),
                emptyAttributes,
                emptyCapabilities,
                emptyOperations),
            new ResourceAttributeSet(
            [
                new(
                    "project.hotReload",
                    false,
                    ResourceDefinitionValueSource.ResourceState),
                new(
                    "project.environmentVariables.CLOUDSHELL_METRIC_INGEST_ENDPOINT.value",
                    "http://127.0.0.1:5011/api/control-plane/v1/metrics/ingest",
                    ResourceDefinitionValueSource.ResourceState)
            ]),
            emptyCapabilities,
            emptyOperations,
            []);

        var startInfo = new AspNetCoreProjectProcessCommandFactory()
            .CreateStartInfo(resource, "/tmp/test-api.csproj");

        Assert.Equal(
            "http://127.0.0.1:5011/api/control-plane/v1/metrics/ingest",
            startInfo.Environment["CLOUDSHELL_METRIC_INGEST_ENDPOINT"]);
    }
}
