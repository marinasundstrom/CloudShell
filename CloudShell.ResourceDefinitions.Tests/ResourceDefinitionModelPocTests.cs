using System.Text.Json;
using CloudShell.ResourceDefinitions.ReferenceProviders;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ResourceDefinitionModelPocTests
{
    [Fact]
    public async Task ModelFlow_CanMoveFromDocumentThroughPersistenceToProjectionAndApplyPlan()
    {
        var authoredDeployment = new ResourceDeploymentDefinition(
            "local-app",
            [CreateExecutableDefinition()],
            EnvironmentId: "local");

        var document = JsonSerializer.Serialize(authoredDeployment);
        var fromDocument = JsonSerializer.Deserialize<ResourceDeploymentDefinition>(document);

        Assert.NotNull(fromDocument);

        var records = fromDocument.Resources
            .Select(ResourceDefinitionRecord.FromDefinition)
            .ToArray();
        var deploymentFromPersistence = new ResourceDeploymentDefinition(
            fromDocument.Name,
            records.Select(record => record.ToDefinition()).ToArray(),
            fromDocument.EnvironmentId,
            fromDocument.Metadata);
        var validation = await CreateGraphPipeline().ValidateAsync(
            deploymentFromPersistence,
            new ResourceDefinitionValidationContext(PrincipalId: "developer"));

        Assert.False(validation.HasErrors);

        var api = validation.FindResource("application.executable:api");
        Assert.NotNull(api);

        var executable = await CreateProjectionResolver()
            .GetResourceProjectionAsync<ExecutableApplicationResource>(
                api.Projection,
                new ResourceProjectionContext("local", "developer"));

        Assert.NotNull(executable);

        var volumes = await executable.GetVolumesAsync();
        var volume = Assert.Single(volumes);
        Assert.Equal("volume:data", volume.Volume);

        var plan = await CreateApplyPlanner().PlanApplyAsync(
            validation,
            new ResourceDefinitionApplyContext("local", "developer"));

        Assert.False(plan.HasErrors);
        Assert.Contains(plan.Steps, step =>
            step.Kind == ResourceDefinitionApplyStepKind.AcceptDefinition &&
            step.Definition?.EffectiveResourceId == "application.executable:api");
        Assert.Contains(plan.Steps, step =>
            step.Kind == ResourceDefinitionApplyStepKind.MaterializeRuntime &&
            step.ResourceId == "application.executable:api");
    }

    private static ResourceDefinition CreateExecutableDefinition() =>
        new(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet"
            },
            Configuration: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [ExecutableApplicationResourceTypeProvider.ConfigurationSection] =
                    ResourceDefinitionJson.FromValue(new ExecutableApplicationConfiguration("dotnet", "run"))
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new("volume:data", "App_Data")
                    ]))
            });

    private static ResourceDefinitionGraphValidationPipeline CreateGraphPipeline() =>
        new(
            new ResourceDefinitionValidationPipeline(
                [new(ExecutableApplicationResourceTypeProvider.ClassId)],
                [new ExecutableApplicationResourceTypeProvider()],
                capabilityProviders: [new VolumeConsumerCapabilityProvider()],
                operationProviders: [new ExecutableStartOperationProvider()],
                capabilityProjectors: [new VolumeConsumerCapabilityProvider()]));

    private static ResourceProjectionResolver CreateProjectionResolver() =>
        new([new ExecutableApplicationResourceProjectionProvider()]);

    private static ResourceDefinitionGraphApplyPlanner CreateApplyPlanner() =>
        new([new ExecutableApplicationResourceTypeProvider()]);

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
