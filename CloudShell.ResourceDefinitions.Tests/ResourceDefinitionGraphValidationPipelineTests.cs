using System.Text.Json;
using CloudShell.ResourceDefinitions.ReferenceProviders;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ResourceDefinitionGraphValidationPipelineTests
{
    [Fact]
    public async Task ValidateAsync_DeploymentDefinition_ValidatesProposedResourceGraph()
    {
        var pipeline = CreateGraphPipeline();
        var worker = CreateExecutableDefinition("worker");
        var api = CreateExecutableDefinition(
            "api",
            dependsOn: [worker.EffectiveResourceId]);
        var deployment = new ResourceDeploymentDefinition(
            "local-app",
            [worker, api],
            EnvironmentId: "local");

        var result = await pipeline.ValidateAsync(
            deployment,
            new ResourceDefinitionValidationContext(PrincipalId: "developer"));

        Assert.False(result.HasErrors);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.Resources.Count);
        Assert.NotNull(result.FindResource(api.EffectiveResourceId));
        Assert.All(result.Resources, resource => Assert.False(resource.HasErrors));
    }

    [Fact]
    public async Task ValidateAsync_Graph_ReturnsMissingDependencyAndDuplicateDiagnostics()
    {
        var pipeline = CreateGraphPipeline();
        var api = CreateExecutableDefinition(
            "api",
            dependsOn: ["application.executable:missing"]);
        var duplicateApi = CreateExecutableDefinition("api");

        var result = await pipeline.ValidateAsync(
            new ResourceDefinitionGraph([api, duplicateApi]),
            new ResourceDefinitionValidationContext());

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.DuplicateResourceDefinition &&
            diagnostic.Target == api.EffectiveResourceId);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceDependencyMissing &&
            diagnostic.Target == api.EffectiveResourceId);
    }

    private static ResourceDefinitionGraphValidationPipeline CreateGraphPipeline() =>
        new(CreateResourcePipeline());

    private static ResourceDefinitionValidationPipeline CreateResourcePipeline() =>
        new(
            [new(ExecutableApplicationResourceTypeProvider.ClassId)],
            [new ExecutableApplicationResourceTypeProvider()],
            operationProviders: [new ExecutableStartOperationProvider()]);

    private static ResourceDefinition CreateExecutableDefinition(
        string name,
        IReadOnlyList<string>? dependsOn = null) =>
        new(
            name,
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            DependsOn: dependsOn,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet"
            },
            Configuration: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [ExecutableApplicationResourceTypeProvider.ConfigurationSection] =
                    ResourceDefinitionJson.FromValue(new ExecutableApplicationConfiguration("dotnet", "run"))
            });

    private sealed class ExecutableStartOperationProvider : IResourceOperationProvider
    {
        public ResourceOperationId OperationId =>
            ExecutableApplicationResourceTypeProvider.Operations.Start;

        public ResourceDefinitionValueSource ResolutionLevel =>
            ResourceDefinitionValueSource.TypeDefinition;

        public bool CanHandle(
            ResolvedResourceDefinition resource,
            ResourceOperationResolution operation) =>
            resource.TypeDefinition.TypeId == ExecutableApplicationResourceTypeProvider.ResourceTypeId &&
            operation.IsAvailable;

        public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
            ResolvedResourceDefinition resource,
            ResourceOperationResolution operation,
            ResourceDefinitionValidationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ResourceDefinitionValidationResult.Success);
    }
}
