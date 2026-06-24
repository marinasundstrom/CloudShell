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

    [Fact]
    public async Task ValidateAsync_Graph_ReturnsMissingCapabilityReferenceDiagnostic()
    {
        var pipeline = CreateVolumeConsumerGraphPipeline();
        var api = CreateExecutableDefinition(
            "api",
            mounts:
            [
                new("storage.volume:missing", "App_Data")
            ]);

        var result = await pipeline.ValidateAsync(
            new ResourceDefinitionGraph([api]),
            new ResourceDefinitionValidationContext());

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceMissing &&
            diagnostic.Target == api.EffectiveResourceId);
    }

    [Fact]
    public async Task ValidateAsync_Graph_ReturnsInvalidCapabilityReferenceDiagnostic()
    {
        var pipeline = CreateVolumeConsumerGraphPipeline();
        var worker = CreateExecutableDefinition("worker");
        var api = CreateExecutableDefinition(
            "api",
            mounts:
            [
                new(worker.EffectiveResourceId, "App_Data")
            ]);

        var result = await pipeline.ValidateAsync(
            new ResourceDefinitionGraph([worker, api]),
            new ResourceDefinitionValidationContext());

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid &&
            diagnostic.Target == api.EffectiveResourceId);
    }

    [Fact]
    public async Task PlanApplyAsync_ValidatedGraph_UsesResourceTypeApplyProviders()
    {
        var validation = await CreateGraphPipeline().ValidateAsync(
            new ResourceDefinitionGraph(
            [
                CreateExecutableDefinition("worker"),
                CreateExecutableDefinition("api")
            ]),
            new ResourceDefinitionValidationContext("local", "developer"));
        var planner = CreateApplyPlanner();

        var plan = await planner.PlanApplyAsync(
            validation,
            new ResourceDefinitionApplyContext("local", "developer"));

        Assert.False(plan.HasErrors);
        Assert.Empty(plan.Diagnostics);
        Assert.Equal(2, plan.Resources.Count);
        Assert.Equal(4, plan.Steps.Count());
        Assert.Contains(plan.Steps, step =>
            step.Kind == ResourceDefinitionApplyStepKind.AcceptDefinition &&
            step.ResourceId == "application.executable:api");
        Assert.Contains(plan.Steps, step =>
            step.Kind == ResourceDefinitionApplyStepKind.MaterializeRuntime &&
            step.ResourceId == "application.executable:worker");
    }

    [Fact]
    public async Task PlanApplyAsync_InvalidGraph_ReturnsValidationDiagnosticsWithoutProviderPlans()
    {
        var validation = await CreateGraphPipeline().ValidateAsync(
            new ResourceDefinitionGraph(
            [
                CreateExecutableDefinition(
                    "api",
                    dependsOn: ["application.executable:missing"])
            ]),
            new ResourceDefinitionValidationContext());
        var planner = CreateApplyPlanner();

        var plan = await planner.PlanApplyAsync(
            validation,
            new ResourceDefinitionApplyContext());

        Assert.True(plan.HasErrors);
        Assert.Empty(plan.Resources);
        Assert.Contains(plan.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceDependencyMissing);
    }

    [Fact]
    public async Task PlanApplyAsync_ReturnsDiagnosticWhenApplyProviderIsMissing()
    {
        var validation = await CreateGraphPipeline().ValidateAsync(
            new ResourceDefinitionGraph([CreateExecutableDefinition("api")]),
            new ResourceDefinitionValidationContext());
        var planner = new ResourceDefinitionGraphApplyPlanner([]);

        var plan = await planner.PlanApplyAsync(
            validation,
            new ResourceDefinitionApplyContext());

        Assert.True(plan.HasErrors);
        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(
            ResourceDefinitionDiagnosticCodes.ResourceDefinitionApplyProviderMissing,
            diagnostic.Code);
        Assert.Equal("application.executable:api", diagnostic.Target);
    }

    private static ResourceDefinitionGraphValidationPipeline CreateGraphPipeline() =>
        new(CreateResourcePipeline());

    private static ResourceDefinitionGraphValidationPipeline CreateVolumeConsumerGraphPipeline() =>
        new(
            new ResourceDefinitionValidationPipeline(
                [
                    ExecutableApplicationResourceTypeProvider.ClassDefinition,
                    LocalVolumeResourceTypeProvider.ClassDefinition
                ],
                [
                    new ExecutableApplicationResourceTypeProvider(),
                    new LocalVolumeResourceTypeProvider()
                ],
                capabilityProviders: [new VolumeConsumerCapabilityProvider()],
                operationProviders: [new ExecutableStartOperationProvider()]),
            [new VolumeConsumerGraphValidator()]);

    private static ResourceDefinitionGraphApplyPlanner CreateApplyPlanner() =>
        new([new ExecutableApplicationResourceTypeProvider()]);

    private static ResourceDefinitionValidationPipeline CreateResourcePipeline() =>
        new(
            [new(ExecutableApplicationResourceTypeProvider.ClassId)],
            [new ExecutableApplicationResourceTypeProvider()],
            operationProviders: [new ExecutableStartOperationProvider()]);

    private static ResourceDefinition CreateExecutableDefinition(
        string name,
        IReadOnlyList<string>? dependsOn = null,
        IReadOnlyList<VolumeMountDefinition>? mounts = null) =>
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
            },
            Capabilities: mounts is null
                ? null
                : new Dictionary<ResourceCapabilityId, JsonElement>
                {
                    [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                        ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(mounts))
                });

    private sealed class ExecutableStartOperationProvider : IResourceOperationProvider
    {
        public ResourceOperationId OperationId =>
            ExecutableApplicationResourceTypeProvider.Operations.Start;

        public ResourceDefinitionValueSource ResolutionLevel =>
            ResourceDefinitionValueSource.TypeDefinition;

        public bool CanHandle(
            Resource resource,
            ResourceOperationResolution operation) =>
            resource.Type.TypeId == ExecutableApplicationResourceTypeProvider.ResourceTypeId &&
            operation.IsAvailable;

        public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
            Resource resource,
            ResourceOperationResolution operation,
            ResourceProviderContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ResourceDefinitionValidationResult.Success);
    }
}
