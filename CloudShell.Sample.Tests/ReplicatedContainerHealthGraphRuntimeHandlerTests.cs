using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.Sample.Tests;

public sealed class ReplicatedContainerHealthGraphRuntimeHandlerTests
{
    [Theory]
    [InlineData(false, ContainerApplicationRuntimeStatus.Stopped)]
    [InlineData(true, ContainerApplicationRuntimeStatus.Running)]
    public async Task ResourceManagerBridge_MapsRuntimeAppRunningState(
        bool isRunning,
        ContainerApplicationRuntimeStatus expectedStatus)
    {
        var bridge = CreateResourceManagerBridge(
            new RecordingResourceManager(),
            new RecordingRunningState { IsRunningResult = isRunning });

        var status = bridge.GetStatus(await CreateGraphAppResourceAsync());

        Assert.Equal(expectedStatus, status);
    }

    [Theory]
    [InlineData("start", "start", true, false)]
    [InlineData("stop", "stop", false, true)]
    [InlineData("restart", "restart", true, true)]
    public async Task ResourceManagerBridge_DelegatesLifecycleToRuntimeApp(
        string graphOperationId,
        string expectedActionId,
        bool expectedStartDependencies,
        bool expectedIgnoreDependentWarning)
    {
        var resourceManager = new RecordingResourceManager();
        var bridge = CreateResourceManagerBridge(resourceManager, new RecordingRunningState());

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            await CreateGraphAppResourceAsync(),
            graphOperationId);

        Assert.Empty(diagnostics);
        var command = Assert.Single(resourceManager.ActionCommands);
        Assert.Equal("application:api", command.ResourceId);
        Assert.Equal(expectedActionId, command.ActionId);
        Assert.Equal(expectedStartDependencies, command.StartDependencies);
        Assert.Equal(expectedIgnoreDependentWarning, command.IgnoreDependentWarning);
    }

    [Fact]
    public async Task ResourceManagerBridge_DelegatesImageAndReplicasToRuntimeApp()
    {
        var resourceManager = new RecordingResourceManager();
        var bridge = CreateResourceManagerBridge(resourceManager, new RecordingRunningState());

        var diagnostics = await bridge.ApplyImageAsync(
            await CreateGraphAppResourceAsync(
                image: "cloudshell-application-api:20260622.3",
                replicas: 5));

        Assert.Empty(diagnostics);
        var command = Assert.Single(resourceManager.ImageCommands);
        Assert.Equal("application:api", command.ResourceId);
        Assert.Equal("cloudshell-application-api:20260622.3", command.Image);
        Assert.False(command.RestartIfRunning);
        Assert.Equal("resource-graph", command.TriggeredBy);
        Assert.Equal(5, command.RequestedReplicas);
    }

    [Fact]
    public async Task ResourceManagerBridge_DelegatesReplicaUpdateToRuntimeApp()
    {
        var resourceManager = new RecordingResourceManager();
        var bridge = CreateResourceManagerBridge(resourceManager, new RecordingRunningState());

        var diagnostics = await bridge.ApplyReplicasAsync(
            await CreateGraphAppResourceAsync(replicas: 2));

        Assert.Empty(diagnostics);
        var command = Assert.Single(resourceManager.ReplicaCommands);
        Assert.Equal("application:api", command.ResourceId);
        Assert.Equal(2, command.Replicas);
        Assert.False(command.RestartIfRunning);
        Assert.Equal("resource-graph", command.TriggeredBy);
    }

    [Fact]
    public async Task GraphOnlyBridge_StartPublishesImageAndRunsGraphReplicaContainers()
    {
        var commandRunner = new RecordingCommandRunner();
        var bridge = new ReplicatedContainerHealthGraphOnlyContainerAppRuntimeBridge(
            commandRunner,
            CreateConfiguration());
        var resource = await CreateGraphAppResourceAsync(replicas: 2, endpointPort: 5092);

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Collection(
            commandRunner.Commands,
            command =>
            {
                Assert.Equal("dotnet", command.FileName);
                Assert.Equal("publish", command.Arguments[0]);
                Assert.Contains("samples/ReplicatedContainerHealth/Api/CloudShell.ReplicatedContainerHealth.Api.csproj", command.Arguments);
                Assert.Contains("-p:ContainerRepository=cloudshell-application-api", command.Arguments);
                Assert.Contains("-p:ContainerImageTag=20260622.2", command.Arguments);
            },
            command => AssertDockerRemove(command, "cloudshell-replicated-health-graph-api-replica-1"),
            command => AssertDockerRun(command, "cloudshell-replicated-health-graph-api-replica-1", replica: 1, expectPublishedEndpoint: true),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-graph-api-replica-2"),
            command => AssertDockerRun(command, "cloudshell-replicated-health-graph-api-replica-2", replica: 2, expectPublishedEndpoint: false));
    }

    [Fact]
    public async Task GraphOnlyBridge_StopRemovesGraphReplicaContainers()
    {
        var commandRunner = new RecordingCommandRunner();
        var bridge = new ReplicatedContainerHealthGraphOnlyContainerAppRuntimeBridge(
            commandRunner,
            CreateConfiguration());
        var resource = await CreateGraphAppResourceAsync(replicas: 2);

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Stop);

        Assert.Empty(diagnostics);
        Assert.Collection(
            commandRunner.Commands,
            command => AssertDockerRemove(command, "cloudshell-replicated-health-graph-api-replica-1"),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-graph-api-replica-2"));
    }

    [Fact]
    public async Task GraphOnlyBridge_GetStatusReturnsRunningWhenAllReplicasAreRunning()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(0, "running", string.Empty));
        commandRunner.Enqueue(new(0, "running", string.Empty));
        var bridge = new ReplicatedContainerHealthGraphOnlyContainerAppRuntimeBridge(
            commandRunner,
            CreateConfiguration());

        var status = bridge.GetStatus(await CreateGraphAppResourceAsync(replicas: 2));

        Assert.Equal(ContainerApplicationRuntimeStatus.Running, status);
        Assert.Collection(
            commandRunner.Commands,
            command => AssertDockerInspect(command, "cloudshell-replicated-health-graph-api-replica-1"),
            command => AssertDockerInspect(command, "cloudshell-replicated-health-graph-api-replica-2"));
    }

    [Fact]
    public async Task GraphOnlyBridge_GetStatusReturnsStoppedWhenAllReplicasAreMissing()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(1, string.Empty, "No such container"));
        commandRunner.Enqueue(new(1, string.Empty, "No such container"));
        var bridge = new ReplicatedContainerHealthGraphOnlyContainerAppRuntimeBridge(
            commandRunner,
            CreateConfiguration());

        var status = bridge.GetStatus(await CreateGraphAppResourceAsync(replicas: 2));

        Assert.Equal(ContainerApplicationRuntimeStatus.Stopped, status);
    }

    [Fact]
    public async Task GraphOnlyBridge_GetStatusReturnsUnknownWhenReplicaStatesAreMixed()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(0, "running", string.Empty));
        commandRunner.Enqueue(new(0, "exited", string.Empty));
        var bridge = new ReplicatedContainerHealthGraphOnlyContainerAppRuntimeBridge(
            commandRunner,
            CreateConfiguration());

        var status = bridge.GetStatus(await CreateGraphAppResourceAsync(replicas: 2));

        Assert.Equal(ContainerApplicationRuntimeStatus.Unknown, status);
    }

    [Fact]
    public async Task GraphOnlyBridge_GetStatusReturnsUnknownWhenDockerProbeTimesOut()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(
            ReplicatedContainerHealthCommandResult.TimeoutExitCode,
            string.Empty,
            "timeout"));
        var bridge = new ReplicatedContainerHealthGraphOnlyContainerAppRuntimeBridge(
            commandRunner,
            CreateConfiguration());

        var status = bridge.GetStatus(await CreateGraphAppResourceAsync(replicas: 1));

        Assert.Equal(ContainerApplicationRuntimeStatus.Unknown, status);
    }

    [Fact]
    public async Task GraphOnlyBridge_ApplyImageRestartsWhenRunning()
    {
        var commandRunner = new RecordingCommandRunner();
        var bridge = new ReplicatedContainerHealthGraphOnlyContainerAppRuntimeBridge(
            commandRunner,
            CreateConfiguration());
        var resource = await CreateGraphAppResourceAsync(replicas: 1);
        await bridge.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Start);
        commandRunner.Commands.Clear();
        commandRunner.Enqueue(new(0, "running", string.Empty));

        var diagnostics = await bridge.ApplyImageAsync(resource);

        Assert.Empty(diagnostics);
        Assert.Collection(
            commandRunner.Commands,
            command => AssertDockerInspect(command, "cloudshell-replicated-health-graph-api-replica-1"),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-graph-api-replica-1"),
            command =>
            {
                Assert.Equal("dotnet", command.FileName);
                Assert.Equal("publish", command.Arguments[0]);
            },
            command => AssertDockerRemove(command, "cloudshell-replicated-health-graph-api-replica-1"),
            command => AssertDockerRun(command, "cloudshell-replicated-health-graph-api-replica-1", replica: 1, expectPublishedEndpoint: false));
    }

    [Fact]
    public async Task Handler_DelegatesMappedGraphApiToBridge()
    {
        var bridge = new RecordingGraphContainerAppRuntimeBridge(ContainerApplicationRuntimeStatus.Running);
        var handler = new ReplicatedContainerHealthGraphRuntimeHandler(bridge);
        var resource = await CreateGraphAppResourceAsync();

        Assert.Equal(ContainerApplicationRuntimeStatus.Running, handler.GetStatus(resource));

        var diagnostics = await handler.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        var command = Assert.Single(bridge.LifecycleCommands);
        Assert.Equal("application.container-app:graph-api", command.Resource.EffectiveResourceId);
        Assert.Equal(ContainerApplicationResourceTypeProvider.Operations.Start, command.OperationId);
    }

    [Fact]
    public async Task Handler_IgnoresUnmappedGraphContainerAppWithoutCallingBridge()
    {
        var bridge = new RecordingGraphContainerAppRuntimeBridge(ContainerApplicationRuntimeStatus.Running);
        var handler = new ReplicatedContainerHealthGraphRuntimeHandler(bridge);
        var resource = await CreateGraphAppResourceAsync(
            name: "other",
            resourceId: "application.container-app:other");

        var diagnostics = await handler.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Empty(bridge.LifecycleCommands);
        Assert.Equal(ContainerApplicationRuntimeStatus.Unknown, handler.GetStatus(resource));
    }

    private static ReplicatedContainerHealthGraphResourceManagerBridge CreateResourceManagerBridge(
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
        string name = "graph-api",
        string resourceId = "application.container-app:graph-api",
        string image = "cloudshell-application-api:20260622.2",
        int replicas = 3,
        int? endpointPort = null)
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
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>
        {
            [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] = image,
            [ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas] = replicas
        };
        if (endpointPort is not null)
        {
            attributes[ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests] =
                ResourceAttributeValue.FromObject(new[]
                {
                    new NetworkingEndpointRequestValue(
                        "http",
                        "http",
                        TargetPort: 8080,
                        Host: "localhost",
                        Port: endpointPort.Value,
                        Exposure: "Local")
                });
        }

        var result = await pipeline.ValidateAsync(
            new ResourceDefinition(
                name,
                ContainerApplicationResourceTypeProvider.ResourceTypeId,
                ResourceId: resourceId,
                Attributes: attributes),
            new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(
            result.HasErrors,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(diagnostic =>
                    $"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message}")));
        return result.Resource;
    }

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Observability:TraceIngestEndpoint"] = "http://host.docker.internal:5011/api/control-plane/v1/traces/ingest",
                ["Observability:MetricIngestEndpoint"] = "http://host.docker.internal:5011/api/control-plane/v1/metrics/ingest"
            })
            .Build();

    private static void AssertDockerRemove(
        RecordingCommand command,
        string containerName)
    {
        Assert.Equal("docker", command.FileName);
        Assert.Equal(["rm", "-f", containerName], command.Arguments);
        Assert.False(command.ThrowOnError);
    }

    private static void AssertDockerInspect(
        RecordingCommand command,
        string containerName)
    {
        Assert.Equal("docker", command.FileName);
        Assert.Equal(
            ["container", "inspect", "--format", "{{.State.Status}}", containerName],
            command.Arguments);
        Assert.False(command.ThrowOnError);
    }

    private static void AssertDockerRun(
        RecordingCommand command,
        string containerName,
        int replica,
        bool expectPublishedEndpoint)
    {
        Assert.Equal("docker", command.FileName);
        Assert.Equal("run", command.Arguments[0]);
        Assert.Contains("--name", command.Arguments);
        Assert.Contains(containerName, command.Arguments);
        Assert.Contains($"CLOUDSHELL_REPLICA_ORDINAL={replica}", command.Arguments);
        Assert.Contains("CLOUDSHELL_RESOURCE_ID=application.container-app:graph-api", command.Arguments);
        Assert.Contains("CLOUDSHELL_TRACE_INGEST_ENDPOINT=http://host.docker.internal:5011/api/control-plane/v1/traces/ingest", command.Arguments);
        Assert.Contains("cloudshell-application-api:20260622.2", command.Arguments);
        if (expectPublishedEndpoint)
        {
            Assert.Contains("127.0.0.1:5092:8080", command.Arguments);
        }
        else
        {
            Assert.DoesNotContain("127.0.0.1:5092:8080", command.Arguments);
        }
    }

    private sealed class RecordingRunningState : IApplicationResourceRunningStateOperations
    {
        public bool IsRunningResult { get; init; }

        public bool IsRunning(string applicationId)
        {
            Assert.Equal("application:api", applicationId);
            return IsRunningResult;
        }
    }

    private sealed class RecordingGraphContainerAppRuntimeBridge(
        ContainerApplicationRuntimeStatus status) : IReplicatedContainerHealthGraphContainerAppRuntimeBridge
    {
        public List<LifecycleCommand> LifecycleCommands { get; } = [];

        public ContainerApplicationRuntimeStatus GetStatus(GraphResource resource) => status;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
            GraphResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleCommands.Add(new(resource, operationId));
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
    }

    private sealed record LifecycleCommand(
        GraphResource Resource,
        ResourceOperationId OperationId);

    private sealed class RecordingCommandRunner : IReplicatedContainerHealthCommandRunner
    {
        private readonly Queue<ReplicatedContainerHealthCommandResult> _results = [];

        public List<RecordingCommand> Commands { get; } = [];

        public void Enqueue(ReplicatedContainerHealthCommandResult result) =>
            _results.Enqueue(result);

        public ReplicatedContainerHealthCommandResult Run(
            string fileName,
            IReadOnlyList<string> arguments,
            bool throwOnError = true,
            TimeSpan? timeout = null) =>
            RunCore(fileName, arguments, throwOnError);

        public Task<ReplicatedContainerHealthCommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            bool throwOnError = true,
            TimeSpan? timeout = null)
        {
            return Task.FromResult(RunCore(fileName, arguments, throwOnError));
        }

        private ReplicatedContainerHealthCommandResult RunCore(
            string fileName,
            IReadOnlyList<string> arguments,
            bool throwOnError)
        {
            Commands.Add(new(fileName, arguments.ToArray(), throwOnError));
            var result = _results.Count == 0
                ? new ReplicatedContainerHealthCommandResult(0, string.Empty, string.Empty)
                : _results.Dequeue();
            if (throwOnError && result.ExitCode != 0)
            {
                throw new InvalidOperationException(result.Error);
            }

            return result;
        }
    }

    private sealed record RecordingCommand(
        string FileName,
        IReadOnlyList<string> Arguments,
        bool ThrowOnError);
}
