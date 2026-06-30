using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.Sample.Tests;

public sealed class ContainerAppDeploymentContainerApplicationRuntimeHandlerTests
{
    [Fact]
    public async Task Handler_DelegatesMappedAppToBridge()
    {
        var bridge = new RecordingContainerApplicationRuntimeBridge(ContainerApplicationRuntimeStatus.Running);
        var handler = new ContainerAppDeploymentContainerApplicationRuntimeHandler(bridge);
        var resource = await CreateAppResourceAsync();

        Assert.Equal(ContainerApplicationRuntimeStatus.Running, handler.GetStatus(resource));

        var diagnostics = await handler.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        var command = Assert.Single(bridge.LifecycleCommands);
        Assert.Equal("application.container-app:sample-api", command.Resource.EffectiveResourceId);
        Assert.Equal(ContainerApplicationResourceTypeProvider.Operations.Start, command.OperationId);
    }

    [Fact]
    public async Task Handler_IgnoresUnmappedAppWithoutCallingBridge()
    {
        var bridge = new RecordingContainerApplicationRuntimeBridge(ContainerApplicationRuntimeStatus.Running);
        var handler = new ContainerAppDeploymentContainerApplicationRuntimeHandler(bridge);
        var resource = await CreateAppResourceAsync(
            name: "other",
            resourceId: "application.container-app:other");

        var diagnostics = await handler.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Empty(bridge.LifecycleCommands);
        Assert.Equal(ContainerApplicationRuntimeStatus.Unknown, handler.GetStatus(resource));
    }

    [Fact]
    public async Task DefaultBridge_AcceptsStateChangesWithoutMaterializingRuntime()
    {
        var bridge = new ContainerAppDeploymentContainerApplicationRuntimeBridge();
        var resource = await CreateAppResourceAsync();

        Assert.Equal(ContainerApplicationRuntimeStatus.Stopped, bridge.GetStatus(resource));

        var lifecycleDiagnostics = await bridge.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Start);
        var imageDiagnostics = await bridge.ApplyImageAsync(resource);
        var replicaDiagnostics = await bridge.ApplyReplicasAsync(resource);

        Assert.Contains(lifecycleDiagnostics, diagnostic =>
            diagnostic.Code == "containerAppDeployment.containerApp.runtimeDeferred");
        Assert.Contains(imageDiagnostics, diagnostic =>
            diagnostic.Code == "containerAppDeployment.containerApp.imageAccepted");
        Assert.Contains(replicaDiagnostics, diagnostic =>
            diagnostic.Code == "containerAppDeployment.containerApp.replicasAccepted");
    }

    private static async Task<ResourceModelResource> CreateAppResourceAsync(
        string name = "sample-api",
        string resourceId = "application.container-app:sample-api",
        string image = "cloudshell/mock-api:20260608.1",
        int replicas = 2)
    {
        IResourceOperationProvider[] operationProviders =
        [
            new ContainerApplicationStartOperationProvider(),
            new ContainerApplicationStopOperationProvider(),
            new ContainerApplicationRestartOperationProvider(),
            new ContainerApplicationImageUpdateOperationProvider(),
            new ContainerApplicationReplicasUpdateOperationProvider()
        ];
        var pipeline = new ResourceDefinitionValidationPipeline(
            [ContainerApplicationResourceTypeProvider.ClassDefinition],
            [new ContainerApplicationResourceTypeProvider()],
            operationProviders: operationProviders,
            operationProjectors: operationProviders.OfType<IResourceOperationProjector>());
        var result = await pipeline.ValidateAsync(
            new ResourceDefinition(
                name,
                ContainerApplicationResourceTypeProvider.ResourceTypeId,
                ResourceId: resourceId,
                Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                {
                    [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] = image,
                    [ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas] = replicas
                }),
            new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(
            result.HasErrors,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(diagnostic =>
                    $"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message}")));
        return result.Resource;
    }

    private sealed class RecordingContainerApplicationRuntimeBridge(
        ContainerApplicationRuntimeStatus status) : IContainerAppDeploymentContainerApplicationRuntimeBridge
    {
        public List<LifecycleCommand> LifecycleCommands { get; } = [];

        public ContainerApplicationRuntimeStatus GetStatus(ResourceModelResource resource) => status;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
            ResourceModelResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleCommands.Add(new(resource, operationId));
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
            ResourceModelResource resource,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
            ResourceModelResource resource,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
    }

    private sealed record LifecycleCommand(
        ResourceModelResource Resource,
        ResourceOperationId OperationId);
}
