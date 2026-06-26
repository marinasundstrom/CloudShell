using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using Microsoft.Extensions.DependencyInjection;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.Sample.Tests;

public sealed class ContainerAppDeploymentGraphContainerApplicationRuntimeHandlerTests
{
    [Theory]
    [InlineData(false, ContainerApplicationRuntimeStatus.Stopped)]
    [InlineData(true, ContainerApplicationRuntimeStatus.Running)]
    public async Task GetStatus_MapsRuntimeAppRunningState(
        bool isRunning,
        ContainerApplicationRuntimeStatus expectedStatus)
    {
        var handler = CreateHandler(
            new RecordingResourceManager(),
            new RecordingRunningState { IsRunningResult = isRunning });

        var status = handler.GetStatus(await CreateGraphAppResourceAsync());

        Assert.Equal(expectedStatus, status);
    }

    [Theory]
    [InlineData("start", "start", true, false)]
    [InlineData("stop", "stop", false, true)]
    [InlineData("restart", "restart", true, true)]
    public async Task ExecuteLifecycle_DelegatesToRuntimeApp(
        string graphOperationId,
        string expectedActionId,
        bool expectedStartDependencies,
        bool expectedIgnoreDependentWarning)
    {
        var resourceManager = new RecordingResourceManager();
        var handler = CreateHandler(resourceManager, new RecordingRunningState());

        var diagnostics = await handler.ExecuteLifecycleAsync(
            await CreateGraphAppResourceAsync(),
            graphOperationId);

        Assert.Empty(diagnostics);
        var command = Assert.Single(resourceManager.ActionCommands);
        Assert.Equal("application:sample-api", command.ResourceId);
        Assert.Equal(expectedActionId, command.ActionId);
        Assert.Equal(expectedStartDependencies, command.StartDependencies);
        Assert.Equal(expectedIgnoreDependentWarning, command.IgnoreDependentWarning);
    }

    [Fact]
    public async Task ApplyImage_DelegatesImageAndReplicasToRuntimeApp()
    {
        var resourceManager = new RecordingResourceManager();
        var handler = CreateHandler(resourceManager, new RecordingRunningState());

        var diagnostics = await handler.ApplyImageAsync(
            await CreateGraphAppResourceAsync(
                image: "cloudshell/mock-api:20260608.4",
                replicas: 5));

        Assert.Empty(diagnostics);
        var command = Assert.Single(resourceManager.ImageCommands);
        Assert.Equal("application:sample-api", command.ResourceId);
        Assert.Equal("cloudshell/mock-api:20260608.4", command.Image);
        Assert.False(command.RestartIfRunning);
        Assert.Equal("resource-graph", command.TriggeredBy);
        Assert.Equal(5, command.RequestedReplicas);
    }

    [Fact]
    public async Task ApplyReplicas_DelegatesReplicasToRuntimeApp()
    {
        var resourceManager = new RecordingResourceManager();
        var handler = CreateHandler(resourceManager, new RecordingRunningState());

        var diagnostics = await handler.ApplyReplicasAsync(
            await CreateGraphAppResourceAsync(replicas: 4));

        Assert.Empty(diagnostics);
        var command = Assert.Single(resourceManager.ReplicaCommands);
        Assert.Equal("application:sample-api", command.ResourceId);
        Assert.Equal(4, command.Replicas);
        Assert.False(command.RestartIfRunning);
        Assert.Equal("resource-graph", command.TriggeredBy);
    }

    private static ContainerAppDeploymentGraphContainerApplicationRuntimeHandler CreateHandler(
        IResourceManager resourceManager,
        IApplicationResourceRunningStateOperations runningState)
    {
        var services = new ServiceCollection();
        services.AddSingleton(resourceManager);
        services.AddSingleton(runningState);
        var serviceProvider = services.BuildServiceProvider();
        return new(serviceProvider.GetRequiredService<IServiceScopeFactory>());
    }

    private static async Task<GraphResource> CreateGraphAppResourceAsync(
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
                "graph-sample-api",
                ContainerApplicationResourceTypeProvider.ResourceTypeId,
                ResourceId: "application.container-app:graph-sample-api",
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

    private sealed class RecordingRunningState : IApplicationResourceRunningStateOperations
    {
        public bool IsRunningResult { get; init; }

        public bool IsRunning(string applicationId)
        {
            Assert.Equal("application:sample-api", applicationId);
            return IsRunningResult;
        }
    }

}
