using System.Text.Json;
using CloudShell.ResourceDefinitions.ReferenceProviders;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ResourceDefinitionModelPocTests
{
    [Fact]
    public async Task ModelFlow_CanMoveFromDocumentThroughPersistenceToProjectionAndApplyPlan()
    {
        var authoredTemplate = new ResourceTemplate(
            "local-app",
            [CreateExecutableDefinition()],
            EnvironmentId: "local");

        var document = JsonSerializer.Serialize(authoredTemplate);
        var fromDocument = JsonSerializer.Deserialize<ResourceTemplate>(document);

        Assert.NotNull(fromDocument);

        var records = fromDocument.Resources
            .Select(ResourceRecord.FromDefinition)
            .ToArray();
        var templateFromPersistence = new ResourceTemplate(
            fromDocument.Name,
            records.Select(record => record.ToDefinition()).ToArray(),
            fromDocument.EnvironmentId,
            fromDocument.Metadata);
        var validation = await CreateGraphPipeline().ValidateAsync(
            templateFromPersistence,
            new ResourceDefinitionValidationContext(PrincipalId: "developer"));

        Assert.False(validation.HasErrors);

        var projectedGraph = await CreateGraphProjectionResolver()
            .ProjectAsync(
                validation,
                new ResourceProjectionContext("local", "developer"));

        Assert.False(projectedGraph.HasErrors);
        var executable = projectedGraph.Find<ExecutableApplicationResource>(
            "application.executable:api");
        Assert.NotNull(executable);

        var volumes = await executable.GetVolumesAsync();
        var volume = Assert.Single(volumes);
        Assert.Equal("volume:data", volume.Volume);
        var startOperation = await executable.GetStartOperationAsync();
        Assert.NotNull(startOperation);
        Assert.True(await startOperation.CanExecuteAsync());

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
                capabilityProjectors: [new VolumeConsumerCapabilityProvider()],
                operationProjectors: [new ExecutableStartOperationProvider()]));

    private static ResourceDefinitionGraphProjectionResolver CreateGraphProjectionResolver() =>
        new(new ResourceProjectionResolver(
            [new ExecutableApplicationResourceProjectionProvider()],
            new ResourceCapabilityResolver([new VolumeConsumerCapabilityProvider()]),
            new ResourceOperationResolver([new ExecutableStartOperationProvider()])));

    private static ResourceDefinitionGraphApplyPlanner CreateApplyPlanner() =>
        new([new ExecutableApplicationResourceTypeProvider()]);

}
