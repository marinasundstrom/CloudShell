using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Text.Json;
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
            CreateConfiguration(replicaCleanupLimit: 2));
        var resource = await CreateGraphAppResourceAsync(
            replicas: 2,
            endpointPort: 5092,
            includeHealthChecks: true);

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
            command => AssertDockerRemove(command, ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateIngressContainerName()),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-graph-api-replica-1"),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-graph-api-replica-2"),
            AssertDockerNetworkCreate,
            command => AssertDockerRun(
                command,
                "cloudshell-replicated-health-graph-api-replica-1",
                replica: 1,
                expectedProbePort: 5192),
            command => AssertDockerRun(
                command,
                "cloudshell-replicated-health-graph-api-replica-2",
                replica: 2,
                expectedProbePort: 5193),
            command => AssertDockerRemove(command, ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateIngressContainerName()),
            command => AssertDockerIngressRun(command, endpointPort: 5092));
    }

    [Fact]
    public async Task GraphOnlyBridge_StartCleansGraphContainersWhenReplicaStartFails()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.EnqueueSuccess(6);
        commandRunner.Enqueue(new(1, string.Empty, "replica failed"));
        var bridge = new ReplicatedContainerHealthGraphOnlyContainerAppRuntimeBridge(
            commandRunner,
            CreateConfiguration(replicaCleanupLimit: 2));
        var resource = await CreateGraphAppResourceAsync(
            replicas: 2,
            endpointPort: 5092,
            includeHealthChecks: true);

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Start);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("replicatedContainerHealth.graphOnlyRuntimeFailed", diagnostic.Code);
        Assert.Contains("replica failed", diagnostic.Message);
        Assert.Collection(
            commandRunner.Commands,
            command => Assert.Equal("dotnet", command.FileName),
            command => AssertDockerRemove(command, ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateIngressContainerName()),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-graph-api-replica-1"),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-graph-api-replica-2"),
            AssertDockerNetworkCreate,
            command => AssertDockerRun(
                command,
                "cloudshell-replicated-health-graph-api-replica-1",
                replica: 1,
                expectedProbePort: 5192),
            command => AssertDockerRun(
                command,
                "cloudshell-replicated-health-graph-api-replica-2",
                replica: 2,
                expectedProbePort: 5193),
            command => AssertDockerRemove(command, ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateIngressContainerName()),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-graph-api-replica-1"),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-graph-api-replica-2"));
    }

    [Fact]
    public async Task GraphOnlyRuntimeProvider_ProjectsHiddenReplicaResourcesFromGraphState()
    {
        var resource = await CreateGraphAppResourceAsync(
            replicas: 2,
            endpointPort: 5092,
            includeHealthChecks: true);
        var resolver = CreateResourceResolver();
        var resolved = resolver.Resolve(resource.State);
        var parentProjection = ResourceModelResourceManagerMapper.ToResourceManagerResource(resolved);

        Assert.Equal(2, parentProjection.ResourceHealthChecks.Count);
        Assert.NotNull(resolved.Attributes.GetObject<NetworkingEndpointRequestValue[]>(
            ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests));

        var graph = new ResourceGraphModel(new InMemoryResourceStateProvider([resource.State]));
        var provider = new ReplicatedContainerHealthGraphOnlyRuntimeResourceProvider(
            graph,
            resolver,
            new RecordingGraphContainerAppRuntimeBridge(ContainerApplicationRuntimeStatus.Running),
            CreateConfiguration());

        var replicas = provider.GetResources();

        Assert.Collection(
            replicas,
            replica => AssertGraphReplicaResource(replica, ordinal: 1, port: 5192),
            replica => AssertGraphReplicaResource(replica, ordinal: 2, port: 5193));
    }

    [Fact]
    public async Task GraphOnlyBridge_StopRemovesGraphReplicaContainers()
    {
        var commandRunner = new RecordingCommandRunner();
        var bridge = new ReplicatedContainerHealthGraphOnlyContainerAppRuntimeBridge(
            commandRunner,
            CreateConfiguration(replicaCleanupLimit: 2));
        var resource = await CreateGraphAppResourceAsync(replicas: 2);

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Stop);

        Assert.Empty(diagnostics);
        Assert.Collection(
            commandRunner.Commands,
            command => AssertDockerRemove(command, ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateIngressContainerName()),
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
            CreateConfiguration(replicaCleanupLimit: 1));
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
            command => AssertDockerRemove(command, ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateIngressContainerName()),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-graph-api-replica-1"),
            command =>
            {
                Assert.Equal("dotnet", command.FileName);
                Assert.Equal("publish", command.Arguments[0]);
            },
            AssertDockerNetworkCreate,
            command => AssertDockerRun(
                command,
                "cloudshell-replicated-health-graph-api-replica-1",
                replica: 1,
                expectedReplicaCount: 1));
    }

    [Fact]
    public async Task GraphOnlyBridge_ApplyReplicasRestartsAndRemovesStaleReplicaContainers()
    {
        var commandRunner = new RecordingCommandRunner();
        var bridge = new ReplicatedContainerHealthGraphOnlyContainerAppRuntimeBridge(
            commandRunner,
            CreateConfiguration(replicaCleanupLimit: 3));
        var resource = await CreateGraphAppResourceAsync(
            replicas: 2,
            endpointPort: 5092);
        commandRunner.Enqueue(new(0, "running", string.Empty));
        commandRunner.Enqueue(new(0, "running", string.Empty));

        var diagnostics = await bridge.ApplyReplicasAsync(resource);

        Assert.Empty(diagnostics);
        Assert.Collection(
            commandRunner.Commands,
            command => AssertDockerInspect(command, "cloudshell-replicated-health-graph-api-replica-1"),
            command => AssertDockerInspect(command, "cloudshell-replicated-health-graph-api-replica-2"),
            command => AssertDockerRemove(command, ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateIngressContainerName()),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-graph-api-replica-1"),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-graph-api-replica-2"),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-graph-api-replica-3"),
            command =>
            {
                Assert.Equal("dotnet", command.FileName);
                Assert.Equal("publish", command.Arguments[0]);
            },
            AssertDockerNetworkCreate,
            command => AssertDockerRun(command, "cloudshell-replicated-health-graph-api-replica-1", replica: 1),
            command => AssertDockerRun(command, "cloudshell-replicated-health-graph-api-replica-2", replica: 2),
            command => AssertDockerRemove(command, ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateIngressContainerName()),
            command => AssertDockerIngressRun(command, endpointPort: 5092));
    }

    [Fact]
    public void GraphOnlyLogProvider_ProjectsReplicaLogSourcesFromGraphState()
    {
        var provider = new ReplicatedContainerHealthGraphOnlyLogProvider(
            new RecordingCommandRunner(),
            new RecordingResourceManagerStore(CreateResourceManagerGraphAppResource(replicas: 2)));

        var sources = provider.GetLogSources();

        Assert.Collection(
            sources,
            source =>
            {
                Assert.Equal("application.container-app:graph-api:replica-1:logs", source.Id);
                Assert.Equal("Replica 1 logs", source.Name);
                Assert.Equal("application.container-app:graph-api", source.ResourceId);
                Assert.Equal(ResourceLogSourceKind.Container, source.Kind);
                Assert.Equal(LogFormat.JsonConsole, source.Format);
            },
            source =>
            {
                Assert.Equal("application.container-app:graph-api:replica-2:logs", source.Id);
                Assert.Equal("Replica 2 logs", source.Name);
                Assert.Equal("application.container-app:graph-api", source.ResourceId);
                Assert.Equal(ResourceLogSourceKind.Container, source.Kind);
                Assert.Equal(LogFormat.JsonConsole, source.Format);
            });
    }

    [Fact]
    public async Task GraphOnlyLogProvider_ReadsReplicaContainerLogs()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(
            0,
            """
            2026-06-27T10:00:00.0000000Z {"message":"Handled demo work","severity":"Information","source":"ReplicatedApi","traceId":"trace-1","spanId":"span-1","state":{"path":"/work"}}

            """,
            string.Empty));
        var provider = new ReplicatedContainerHealthGraphOnlyLogProvider(
            commandRunner,
            new RecordingResourceManagerStore(CreateResourceManagerGraphAppResource(replicas: 2)));

        var entries = await provider.ReadLogSourceAsync(
            ReplicatedContainerHealthGraphOnlyLogProvider.GetLogSourceId(2),
            maxEntries: 10);

        var command = Assert.Single(commandRunner.Commands);
        Assert.Equal("docker", command.FileName);
        Assert.Equal(
            [
                "logs",
                "--timestamps",
                "--tail",
                "10",
                "cloudshell-replicated-health-graph-api-replica-2"
            ],
            command.Arguments);
        Assert.False(command.ThrowOnError);

        var entry = Assert.Single(entries);
        Assert.Equal(new DateTimeOffset(2026, 6, 27, 10, 0, 0, TimeSpan.Zero), entry.Timestamp);
        Assert.Equal("Handled demo work", entry.Message);
        Assert.Equal("Information", entry.Severity);
        Assert.Equal("ReplicatedApi", entry.Source);
        Assert.Equal("trace-1", entry.TraceId);
        Assert.Equal("span-1", entry.SpanId);
        Assert.NotNull(entry.Attributes);
        Assert.Equal("/work", entry.Attributes["path"]);
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

    [Fact]
    public async Task GraphOnlyDescriptorProvider_MarksGraphApiAsControlPlaneScopedRuntimeWorkload()
    {
        var provider = new ReplicatedContainerHealthGraphOnlyOrchestrationDescriptorProvider();
        var resource = CreateResourceManagerGraphAppResource(replicas: 3);

        Assert.True(provider.CanDescribe(resource));

        var descriptor = await provider.DescribeAsync(
            resource,
            new ResourceOrchestrationDescriptorContext(null, null, null!));
        var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(workload);
        Assert.Equal(ResourceWorkloadKind.LocalExecutable, workload.Kind);
        Assert.Equal(ResourceLifetime.ControlPlaneScoped, workload.Lifetime);
        Assert.Equal("graph-api", workload.Name);
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
        int? endpointPort = null,
        bool includeHealthChecks = false)
    {
        var operationProviders = CreateContainerApplicationOperationProviders();
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
                Attributes: attributes,
                Capabilities: includeHealthChecks
                    ? new Dictionary<ResourceCapabilityId, JsonElement>
                    {
                        [ResourceHealthCheckCapabilityIds.HealthChecks] =
                            ResourceDefinitionJson.FromValue(new ResourceHealthCheckDefinitionSet(
                            [
                                ResourceHealthCheckDefinition.Http("/health", endpointName: "http"),
                                ResourceHealthCheckDefinition.HttpLiveness("/alive", endpointName: "http", name: "alive")
                            ]))
                    }
                    : null),
            new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(
            result.HasErrors,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(diagnostic =>
                    $"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message}")));
        return result.Resource;
    }

    private static ResourceResolver CreateResourceResolver()
    {
        return new ResourceResolver(
            [ContainerApplicationResourceTypeProvider.ClassDefinition],
            [new ContainerApplicationResourceTypeProvider().TypeDefinition],
            attributeValueShapeProviders: [new NetworkingEndpointShapeProvider()]);
    }

    private static IResourceOperationProvider[] CreateContainerApplicationOperationProviders() =>
    [
        new ContainerApplicationStartOperationProvider(),
        new ContainerApplicationStopOperationProvider(),
        new ContainerApplicationRestartOperationProvider(),
        new ContainerApplicationImageUpdateOperationProvider(),
        new ContainerApplicationReplicasUpdateOperationProvider()
    ];

    private static CloudShell.Abstractions.ResourceManager.Resource CreateResourceManagerGraphAppResource(
        int replicas) =>
        new(
            "application.container-app:graph-api",
            "graph-api",
            "container",
            "Resource model",
            "local",
            CloudShell.Abstractions.ResourceManager.ResourceState.Running,
            [],
            "v1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: "application.container-app",
            Attributes: new Dictionary<string, string>
            {
                ["container.replicas"] = replicas.ToString(CultureInfo.InvariantCulture)
            });

    private static IConfiguration CreateConfiguration(int? replicaCleanupLimit = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Observability:TraceIngestEndpoint"] = "http://host.docker.internal:5011/api/control-plane/v1/traces/ingest",
            ["Observability:MetricIngestEndpoint"] = "http://host.docker.internal:5011/api/control-plane/v1/metrics/ingest"
        };
        if (replicaCleanupLimit is not null)
        {
            values["ReplicatedContainerHealth:GraphOnlyReplicaCleanupLimit"] = replicaCleanupLimit.Value.ToString();
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

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

    private static void AssertDockerNetworkCreate(RecordingCommand command)
    {
        Assert.Equal("docker", command.FileName);
        Assert.Equal(["network", "create", "cloudshell"], command.Arguments);
        Assert.False(command.ThrowOnError);
    }

    private static void AssertDockerRun(
        RecordingCommand command,
        string containerName,
        int replica,
        int? expectedProbePort = null,
        int expectedReplicaCount = 2)
    {
        Assert.Equal("docker", command.FileName);
        Assert.Equal("run", command.Arguments[0]);
        Assert.Contains("--name", command.Arguments);
        Assert.Contains(containerName, command.Arguments);
        Assert.Contains("--rm", command.Arguments);
        Assert.Contains("--network", command.Arguments);
        Assert.Contains("cloudshell", command.Arguments);
        Assert.Contains("--network-alias", command.Arguments);
        Assert.Contains(
            ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateReplicaNetworkAlias(replica),
            command.Arguments);
        Assert.Contains($"CLOUDSHELL_REPLICA_ORDINAL={replica}", command.Arguments);
        Assert.Contains(
            $"CLOUDSHELL_RESOURCE_ID={ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateReplicaResourceId(replica)}",
            command.Arguments);
        Assert.Contains(
            $"OTEL_SERVICE_NAME=replicated-container-health-graph-api-replica-{replica.ToString(CultureInfo.InvariantCulture)}",
            command.Arguments);
        Assert.Contains(
            CreateExpectedOtelResourceAttributes(replica, expectedReplicaCount),
            command.Arguments);
        Assert.Contains("CLOUDSHELL_TRACE_INGEST_ENDPOINT=http://host.docker.internal:5011/api/control-plane/v1/traces/ingest", command.Arguments);
        Assert.Contains("CLOUDSHELL_METRIC_INGEST_ENDPOINT=http://host.docker.internal:5011/api/control-plane/v1/metrics/ingest", command.Arguments);
        Assert.Contains("cloudshell-application-api:20260622.2", command.Arguments);
        Assert.DoesNotContain("127.0.0.1:5092:8080", command.Arguments);

        if (expectedProbePort is not null)
        {
            Assert.Contains(
                $"127.0.0.1:{expectedProbePort.Value.ToString(CultureInfo.InvariantCulture)}:8080",
                command.Arguments);
        }
    }

    private static void AssertDockerIngressRun(
        RecordingCommand command,
        int endpointPort)
    {
        Assert.Equal("docker", command.FileName);
        Assert.Equal("run", command.Arguments[0]);
        Assert.Contains("--name", command.Arguments);
        Assert.Contains(ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateIngressContainerName(), command.Arguments);
        Assert.DoesNotContain("--rm", command.Arguments);
        Assert.Contains("--network", command.Arguments);
        Assert.Contains("cloudshell", command.Arguments);
        Assert.Contains(
            $"127.0.0.1:{endpointPort.ToString(CultureInfo.InvariantCulture)}:{endpointPort.ToString(CultureInfo.InvariantCulture)}/tcp",
            command.Arguments);
        Assert.Contains("traefik:v3.0", command.Arguments);
        Assert.Contains("--providers.file.directory=/etc/traefik/dynamic", command.Arguments);
        Assert.Contains("--providers.file.watch=true", command.Arguments);
        Assert.Contains(
            $"--entrypoints.http.address=:{endpointPort.ToString(CultureInfo.InvariantCulture)}",
            command.Arguments);
    }

    private static string CreateExpectedOtelResourceAttributes(
        int replica,
        int replicaCount)
    {
        var resourceId = ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateReplicaResourceId(replica);
        var containerName = ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateReplicaContainerName(replica);
        return string.Join(
            ',',
            $"OTEL_RESOURCE_ATTRIBUTES=service.instance.id={resourceId}",
            $"cloudshell.resource.id={resourceId}",
            "cloudshell.resource.type=runtime.container",
            $"telemetry.scope.resourceId={ReplicatedContainerHealthGraphOnlyRuntimeConventions.GraphApiResourceId}",
            $"telemetry.scope.name=Replica {replica.ToString(CultureInfo.InvariantCulture)}",
            "telemetry.scope.kind=runtime",
            $"runtime.replica.ordinal={replica.ToString(CultureInfo.InvariantCulture)}",
            $"runtime.replica.count={replicaCount.ToString(CultureInfo.InvariantCulture)}",
            $"runtime.container.name={containerName}");
    }

    private static void AssertGraphReplicaResource(
        CloudShell.Abstractions.ResourceManager.Resource replica,
        int ordinal,
        int port)
    {
        Assert.Equal(
            ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateReplicaResourceId(ordinal),
            replica.Id);
        Assert.Equal(ReplicatedContainerHealthGraphOnlyRuntimeConventions.GraphApiResourceId, replica.ParentResourceId);
        Assert.Equal(ResourceManagementMode.RuntimeManaged, replica.ManagementMode);
        Assert.Equal(ResourceVisibility.Hidden, replica.Visibility);
        Assert.Equal(CloudShell.Abstractions.ResourceManager.ResourceState.Running, replica.State);
        Assert.Equal("runtime.container", replica.TypeId);
        Assert.Equal("containerReplica", replica.ResourceAttributes[ResourceAttributeNames.RuntimeKind]);
        Assert.Equal(
            ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateReplicaContainerName(ordinal),
            replica.ResourceAttributes[ResourceAttributeNames.RuntimeContainerName]);
        Assert.Equal(ordinal.ToString(CultureInfo.InvariantCulture), replica.ResourceAttributes[ResourceAttributeNames.RuntimeReplicaOrdinal]);
        Assert.Equal("2", replica.ResourceAttributes[ResourceAttributeNames.RuntimeReplicaCount]);
        Assert.Equal(2, replica.ResourceHealthChecks.Count);
        Assert.Contains(replica.ResourceHealthChecks, check => check.Type == ResourceProbeType.Health);
        Assert.Contains(replica.ResourceHealthChecks, check => check.Type == ResourceProbeType.Liveness);
        Assert.True(replica.EffectiveObservability.Logs);
        Assert.True(replica.EffectiveObservability.Traces);
        Assert.True(replica.EffectiveObservability.Metrics);
        Assert.Equal(
            $"replicated-container-health-graph-api-replica-{ordinal.ToString(CultureInfo.InvariantCulture)}",
            replica.EffectiveObservability.ServiceName);
        Assert.Equal(
            ReplicatedContainerHealthGraphOnlyRuntimeConventions.GraphApiResourceId,
            replica.EffectiveObservability.Attributes["telemetry.scope.resourceId"]);
        Assert.Equal(
            ordinal.ToString(CultureInfo.InvariantCulture),
            replica.EffectiveObservability.Attributes["runtime.replica.ordinal"]);
        var scope = Assert.Single(replica.EffectiveObservability.TelemetryScopes);
        Assert.Equal(ReplicatedContainerHealthGraphOnlyRuntimeConventions.GraphApiResourceId, scope.ScopeResourceId);
        Assert.Equal($"Replica {ordinal.ToString(CultureInfo.InvariantCulture)}", scope.Name);
        Assert.Equal("runtime", scope.Kind);
        var mapping = Assert.Single(replica.ResourceEndpointNetworkMappings);
        Assert.Equal($"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}", mapping.Address);
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

    private sealed class RecordingResourceManagerStore(
        CloudShell.Abstractions.ResourceManager.Resource resource) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => [];

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public IReadOnlyList<CloudShell.Abstractions.ResourceManager.Resource> GetAvailableResources() => [resource];

        public IReadOnlyList<CloudShell.Abstractions.ResourceManager.Resource> GetResources() => [resource];

        public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() => [];

        public CloudShell.Abstractions.ResourceManager.ResourceClass? GetResourceTypeClass(string resourceType) =>
            CloudShell.Abstractions.ResourceManager.ResourceClass.Container;

        public CloudShell.Abstractions.ResourceManager.Resource? GetResource(string id) =>
            string.Equals(id, resource.Id, StringComparison.OrdinalIgnoreCase)
                ? resource
                : null;

        public IReadOnlyList<CloudShell.Abstractions.ResourceManager.Resource> GetChildren(string resourceId) => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) => true;
    }

    private sealed class RecordingCommandRunner : IReplicatedContainerHealthCommandRunner
    {
        private readonly Queue<ReplicatedContainerHealthCommandResult> _results = [];

        public List<RecordingCommand> Commands { get; } = [];

        public void Enqueue(ReplicatedContainerHealthCommandResult result) =>
            _results.Enqueue(result);

        public void EnqueueSuccess(int count)
        {
            for (var index = 0; index < count; index++)
            {
                Enqueue(new(0, string.Empty, string.Empty));
            }
        }

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
