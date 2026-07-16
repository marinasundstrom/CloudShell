using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;
using GraphResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.Sample.Tests;

public sealed class ReplicatedContainerHealthContainerAppRuntimeHandlerTests
{
    [Fact]
    public async Task RuntimeBridge_StartPublishesImageAndRunsGraphReplicaContainers()
    {
        var commandRunner = new RecordingCommandRunner();
        var options = CreateRuntimeOptions();
        var bridge = CreateRuntimeBridge(
            commandRunner,
            CreateConfiguration(replicaCleanupLimit: 2),
            options: options);
        var resource = await CreateGraphAppResourceAsync(
            replicas: 2,
            endpointPort: 5092,
            includeHealthChecks: true);
        var definition = options.Applications[LocalDockerContainerApplicationRuntimeConventions.ApiResourceId];
        definition.ContainerPublishOperatingSystem = "linux";
        definition.ContainerPublishArchitecture = "arm64";

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
                AssertCommandOption(command, "--os", "linux");
                AssertCommandOption(command, "--arch", "arm64");
                Assert.Contains("-p:ContainerRepository=cloudshell-application-api", command.Arguments);
                Assert.Contains("-p:ContainerImageTag=20260622.2", command.Arguments);
                Assert.Equal(definition.MaterializationCommandTimeout, command.Timeout);
            },
            command => AssertDockerRemove(command, LocalDockerContainerApplicationRuntimeConventions.CreateIngressContainerName()),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-api-replica-1"),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-api-replica-2"),
            AssertDockerNetworkCreate,
            command => AssertDockerRun(
                command,
                "cloudshell-replicated-health-api-replica-1",
                replica: 1,
                expectedProbePort: 5192),
            command => AssertDockerRun(
                command,
                "cloudshell-replicated-health-api-replica-2",
                replica: 2,
                expectedProbePort: 5193),
            command => AssertDockerIngressRun(command, endpointPort: 5092));
    }

    [Fact]
    public async Task RuntimeBridge_StartReturnsDiagnosticWhenImageMaterializationTimesOut()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(
            LocalContainerApplicationCommandResult.TimeoutExitCode,
            string.Empty,
            "publish timed out"));
        var options = CreateRuntimeOptions();
        var definition = options.Applications[LocalDockerContainerApplicationRuntimeConventions.ApiResourceId];
        definition.MaterializationCommandTimeout = TimeSpan.FromSeconds(7);
        var bridge = CreateRuntimeBridge(
            commandRunner,
            CreateConfiguration(),
            options: options);

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            await CreateGraphAppResourceAsync(replicas: 1),
            ContainerApplicationResourceTypeProvider.Operations.Start);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("localDockerContainerApplication.runtimeFailed", diagnostic.Code);
        Assert.Contains("starting the app", diagnostic.Message);
        Assert.Contains(LocalDockerContainerApplicationRuntimeConventions.ApiResourceId, diagnostic.Message);
        Assert.Contains("publish timed out", diagnostic.Message);
        var command = Assert.Single(commandRunner.Commands);
        Assert.Equal("dotnet", command.FileName);
        Assert.Equal("publish", command.Arguments[0]);
        Assert.Equal(definition.MaterializationCommandTimeout, command.Timeout);
    }

    [Fact]
    public async Task RuntimeBridge_StartRunsImageBackedContainerAppWithoutPerAppRuntimeConfiguration()
    {
        var commandRunner = new RecordingCommandRunner();
        var bridge = CreateRuntimeBridge(
            commandRunner,
            CreateConfiguration(replicaCleanupLimit: 1),
            options: new LocalDockerContainerApplicationRuntimeOptions());
        var resource = await CreateGraphAppResourceAsync(
            name: "worker",
            resourceId: "application.container-app:worker",
            image: "redis:7.2-alpine",
            replicas: 1);

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Start);

        var definition = LocalDockerContainerApplicationRuntimeDefinition.CreateDefault(resource.EffectiveResourceId);
        Assert.Empty(diagnostics);
        Assert.DoesNotContain(commandRunner.Commands, command => command.FileName == "dotnet");
        Assert.Collection(
            commandRunner.Commands,
            command => AssertDockerRemove(command, LocalDockerContainerApplicationRuntimeConventions.CreateIngressContainerName(definition)),
            command => AssertDockerRemove(command, LocalDockerContainerApplicationRuntimeConventions.CreateReplicaContainerName(definition, 1)),
            AssertDockerNetworkCreate,
            command =>
            {
                Assert.Equal("docker", command.FileName);
                Assert.Equal("run", command.Arguments[0]);
                Assert.Contains(LocalDockerContainerApplicationRuntimeConventions.CreateReplicaContainerName(definition, 1), command.Arguments);
                Assert.Contains("redis:7.2-alpine", command.Arguments);
            });
    }

    [Fact]
    public async Task RuntimeBridge_StartPublishesProjectPathFromResourceWithoutPerAppRuntimeConfiguration()
    {
        var commandRunner = new RecordingCommandRunner();
        var bridge = CreateRuntimeBridge(
            commandRunner,
            CreateConfiguration(replicaCleanupLimit: 1),
            options: new LocalDockerContainerApplicationRuntimeOptions());
        var resource = await CreateGraphAppResourceAsync(
            name: "api",
            resourceId: "application.container-app:api",
            replicas: 1,
            projectPath: "src/Api/Api.csproj");

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
                Assert.Contains("src/Api/Api.csproj", command.Arguments);
                AssertCommandOption(command, "--os", "linux");
                Assert.False(string.IsNullOrWhiteSpace(GetCommandOption(command, "--arch")));
                Assert.Contains("-p:ContainerRepository=cloudshell-application-api", command.Arguments);
                Assert.Contains("-p:ContainerImageTag=20260622.2", command.Arguments);
            },
            command => AssertDockerRemove(command, "cloudshell-application-container-app-api-ingress"),
            command => AssertDockerRemove(command, "cloudshell-application-container-app-api-replica-1"),
            AssertDockerNetworkCreate,
            command =>
            {
                Assert.Equal("docker", command.FileName);
                Assert.Equal("run", command.Arguments[0]);
                Assert.Contains("cloudshell-application-container-app-api-replica-1", command.Arguments);
            });
    }

    [Fact]
    public async Task RuntimeBridge_StartBuildsJavaProjectBeforeDockerBuildForContainerApp()
    {
        var commandRunner = new RecordingCommandRunner();
        var bridge = CreateRuntimeBridge(
            commandRunner,
            CreateConfiguration(replicaCleanupLimit: 1),
            options: new LocalDockerContainerApplicationRuntimeOptions());
        var resource = await CreateGraphAppResourceAsync(
            name: "api",
            resourceId: "application.container-app:api",
            replicas: 1,
            containerBuildContext: "src/api",
            javaProjectPath: "src/api",
            javaBuildTool: JavaAppBuildTools.Maven,
            javaBuildArguments: "clean package -DskipTests");

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.True(commandRunner.Commands.Count >= 2);
        Assert.Equal("mvn", commandRunner.Commands[0].FileName);
        Assert.Equal(["clean", "package", "-DskipTests"], commandRunner.Commands[0].Arguments);
        Assert.Equal("src/api", commandRunner.Commands[0].WorkingDirectory);
        Assert.Equal("docker", commandRunner.Commands[1].FileName);
        Assert.Equal("build", commandRunner.Commands[1].Arguments[0]);
        Assert.Contains("src/api", commandRunner.Commands[1].Arguments);
    }

    [Fact]
    public async Task RuntimeBridge_DefaultConfigurationScopesDockerNamesByHostInstance()
    {
        var firstBridge = CreateRuntimeBridge(
            new RecordingCommandRunner(),
            CreateConfiguration(urls: "http://localhost:5011"),
            options: new LocalDockerContainerApplicationRuntimeOptions());
        var secondBridge = CreateRuntimeBridge(
            new RecordingCommandRunner(),
            CreateConfiguration(urls: "http://localhost:64178"),
            options: new LocalDockerContainerApplicationRuntimeOptions());
        var resource = await CreateGraphAppResourceAsync(
            resourceId: "application.container-app:signalr-api");

        Assert.True(firstBridge.TryResolveDefinition(resource, out var firstDefinition));
        Assert.True(secondBridge.TryResolveDefinition(resource, out var secondDefinition));

        Assert.NotEqual(firstDefinition.IngressContainerName, secondDefinition.IngressContainerName);
        Assert.NotEqual(firstDefinition.ReplicaContainerNamePrefix, secondDefinition.ReplicaContainerNamePrefix);
        Assert.NotEqual(
            firstDefinition.IngressConfigurationDirectory,
            secondDefinition.IngressConfigurationDirectory);
        Assert.Equal(firstDefinition.ReplicaResourceIdPrefix, secondDefinition.ReplicaResourceIdPrefix);
        Assert.StartsWith("cloudshell-rt-", firstDefinition.IngressContainerName);
        Assert.Contains("signalr-api", firstDefinition.IngressContainerName);
        Assert.True(
            LocalDockerContainerApplicationRuntimeConventions
                .CreateReplicaNetworkAlias(firstDefinition, 1)
                .Length <= 63);
    }

    [Fact]
    public async Task RuntimeBridge_StartCleansGraphContainersWhenReplicaStartFails()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.EnqueueSuccess(6);
        commandRunner.Enqueue(new(1, string.Empty, "replica failed"));
        var bridge = CreateRuntimeBridge(
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
        Assert.Equal("localDockerContainerApplication.runtimeFailed", diagnostic.Code);
        Assert.Contains("starting the app", diagnostic.Message);
        Assert.Contains(LocalDockerContainerApplicationRuntimeConventions.ApiResourceId, diagnostic.Message);
        Assert.Contains("replica failed", diagnostic.Message);
        Assert.Collection(
            commandRunner.Commands,
            command => Assert.Equal("dotnet", command.FileName),
            command => AssertDockerRemove(command, LocalDockerContainerApplicationRuntimeConventions.CreateIngressContainerName()),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-api-replica-1"),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-api-replica-2"),
            AssertDockerNetworkCreate,
            command => AssertDockerRun(
                command,
                "cloudshell-replicated-health-api-replica-1",
                replica: 1,
                expectedProbePort: 5192),
            command => AssertDockerRun(
                command,
                "cloudshell-replicated-health-api-replica-2",
                replica: 2,
                expectedProbePort: 5193),
            command => AssertDockerRemove(command, LocalDockerContainerApplicationRuntimeConventions.CreateIngressContainerName()),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-api-replica-1"),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-api-replica-2"));
    }

    [Fact]
    public async Task RuntimeProvider_ProjectsHiddenReplicaResourcesFromGraphState()
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
        var provider = new LocalDockerContainerApplicationRuntimeResourceProvider(
            graph,
            resolver,
            new RecordingContainerAppRuntimeBridge(ContainerApplicationRuntimeStatus.Running),
            CreateConfiguration());

        var replicas = provider.GetResources();
        var expectedServiceId = ResourceOrchestratorReplicaGroups.CreateDefaultServiceName(resource.EffectiveResourceId);
        var expectedRevisionId = LocalDockerContainerApplicationRuntimeConventions.ResolveRuntimeRevisionId(resource);
        var expectedReplicaGroupId = LocalDockerContainerApplicationRuntimeConventions.ResolveReplicaGroupId(resource);

        Assert.Collection(
            replicas,
            replica => AssertGraphReplicaResource(
                replica,
                ordinal: 1,
                port: 5192,
                expectedServiceId,
                expectedReplicaGroupId,
                expectedRevisionId),
            replica => AssertGraphReplicaResource(
                replica,
                ordinal: 2,
                port: 5193,
                expectedServiceId,
                expectedReplicaGroupId,
                expectedRevisionId));
    }

    [Fact]
    public async Task RuntimeBridge_StopRemovesGraphReplicaContainers()
    {
        var commandRunner = new RecordingCommandRunner();
        var bridge = CreateRuntimeBridge(
            commandRunner,
            CreateConfiguration(replicaCleanupLimit: 2));
        var resource = await CreateGraphAppResourceAsync(replicas: 2);

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Stop);

        Assert.Empty(diagnostics);
        Assert.Collection(
            commandRunner.Commands,
            command => AssertDockerRemove(command, LocalDockerContainerApplicationRuntimeConventions.CreateIngressContainerName()),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-api-replica-1"),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-api-replica-2"));
    }

    [Fact]
    public async Task RuntimeBridge_GetStatusReturnsRunningWhenAllReplicasAreRunning()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(0, "running", string.Empty));
        commandRunner.Enqueue(new(0, "running", string.Empty));
        var bridge = CreateRuntimeBridge(
            commandRunner,
            CreateConfiguration());

        var status = bridge.GetStatus(await CreateGraphAppResourceAsync(replicas: 2));

        Assert.Equal(ContainerApplicationRuntimeStatus.Running, status);
        Assert.Collection(
            commandRunner.Commands,
            command => AssertDockerInspect(command, "cloudshell-replicated-health-api-replica-1"),
            command => AssertDockerInspect(command, "cloudshell-replicated-health-api-replica-2"));
    }

    [Fact]
    public async Task RuntimeBridge_GetStatusReturnsStoppedWhenAllReplicasAreMissing()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(1, string.Empty, "No such container"));
        commandRunner.Enqueue(new(1, string.Empty, "No such container"));
        var bridge = CreateRuntimeBridge(
            commandRunner,
            CreateConfiguration());

        var status = bridge.GetStatus(await CreateGraphAppResourceAsync(replicas: 2));

        Assert.Equal(ContainerApplicationRuntimeStatus.Stopped, status);
    }

    [Fact]
    public async Task RuntimeBridge_GetStatusReturnsUnknownWhenReplicaStatesAreMixed()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(0, "running", string.Empty));
        commandRunner.Enqueue(new(0, "exited", string.Empty));
        var bridge = CreateRuntimeBridge(
            commandRunner,
            CreateConfiguration());

        var status = bridge.GetStatus(await CreateGraphAppResourceAsync(replicas: 2));

        Assert.Equal(ContainerApplicationRuntimeStatus.Unknown, status);
    }

    [Fact]
    public async Task RuntimeBridge_GetStatusReturnsUnknownWhenDockerProbeTimesOut()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(
            LocalContainerApplicationCommandResult.TimeoutExitCode,
            string.Empty,
            "timeout"));
        var bridge = CreateRuntimeBridge(
            commandRunner,
            CreateConfiguration());

        var status = bridge.GetStatus(await CreateGraphAppResourceAsync(replicas: 1));

        Assert.Equal(ContainerApplicationRuntimeStatus.Unknown, status);
    }

    [Fact]
    public async Task RuntimeBridge_GetStatusReturnsUnknownWhenContainerRuntimeIsUnavailable()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(
            LocalContainerApplicationCommandResult.UnavailableExitCode,
            string.Empty,
            "Docker executable 'docker' is unavailable."));
        var bridge = CreateRuntimeBridge(
            commandRunner,
            CreateConfiguration());

        var status = bridge.GetStatus(await CreateGraphAppResourceAsync(replicas: 1));

        Assert.Equal(ContainerApplicationRuntimeStatus.Unknown, status);
    }

    [Fact]
    public async Task RuntimeBridge_GetStatusKeepsLastStableStateWhenDockerProbeTimesOut()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(0, "running", string.Empty));
        commandRunner.Enqueue(new(
            LocalContainerApplicationCommandResult.TimeoutExitCode,
            string.Empty,
            "timeout"));
        var bridge = CreateRuntimeBridge(
            commandRunner,
            CreateConfiguration(statusCacheMilliseconds: 0));
        var resource = await CreateGraphAppResourceAsync(replicas: 1);

        var initialStatus = bridge.GetStatus(resource);
        var statusAfterTimeout = bridge.GetStatus(resource);

        Assert.Equal(ContainerApplicationRuntimeStatus.Running, initialStatus);
        Assert.Equal(ContainerApplicationRuntimeStatus.Running, statusAfterTimeout);
    }

    [Fact]
    public async Task RuntimeBridge_GetStatusDoesNotUseLastStableStateForMixedReplicaStates()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(0, "running", string.Empty));
        commandRunner.Enqueue(new(0, "running", string.Empty));
        commandRunner.Enqueue(new(0, "running", string.Empty));
        commandRunner.Enqueue(new(0, "exited", string.Empty));
        var bridge = CreateRuntimeBridge(
            commandRunner,
            CreateConfiguration(statusCacheMilliseconds: 0));
        var resource = await CreateGraphAppResourceAsync(replicas: 2);

        var initialStatus = bridge.GetStatus(resource);
        var statusAfterMixedState = bridge.GetStatus(resource);

        Assert.Equal(ContainerApplicationRuntimeStatus.Running, initialStatus);
        Assert.Equal(ContainerApplicationRuntimeStatus.Unknown, statusAfterMixedState);
    }

    [Fact]
    public async Task RuntimeBridge_ApplyImageReplacesReplicasWithoutRemovingIngress()
    {
        var commandRunner = new RecordingCommandRunner();
        var bridge = CreateRuntimeBridge(
            commandRunner,
            CreateConfiguration(replicaCleanupLimit: 2));
        var resource = await CreateGraphAppResourceAsync(
            replicas: 2,
            endpointPort: 5092);
        commandRunner.Enqueue(new(0, "running", string.Empty));
        commandRunner.Enqueue(new(0, "running", string.Empty));
        commandRunner.EnqueueSuccess(6);
        commandRunner.Enqueue(new(0, "running", string.Empty));

        var diagnostics = await bridge.ApplyImageAsync(resource);

        Assert.Empty(diagnostics);
        Assert.Collection(
            commandRunner.Commands,
            command => AssertDockerInspect(command, "cloudshell-replicated-health-api-replica-1"),
            command => AssertDockerInspect(command, "cloudshell-replicated-health-api-replica-2"),
            command =>
            {
                Assert.Equal("dotnet", command.FileName);
                Assert.Equal("publish", command.Arguments[0]);
            },
            AssertDockerNetworkCreate,
            command => AssertDockerRemove(command, "cloudshell-replicated-health-api-replica-1"),
            command => AssertDockerRun(
                command,
                "cloudshell-replicated-health-api-replica-1",
                replica: 1,
                expectedProbePort: 5192),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-api-replica-2"),
            command => AssertDockerRun(
                command,
                "cloudshell-replicated-health-api-replica-2",
                replica: 2,
                expectedProbePort: 5193),
            command => AssertDockerInspect(command, LocalDockerContainerApplicationRuntimeConventions.CreateIngressContainerName()));
    }

    [Fact]
    public async Task RuntimeBridge_ApplyReplicasAddsReplicasBeforeUpdatingIngress()
    {
        var commandRunner = new RecordingCommandRunner();
        var bridge = CreateRuntimeBridge(
            commandRunner,
            CreateConfiguration(replicaCleanupLimit: 2));
        var resource = await CreateGraphAppResourceAsync(
            replicas: 4,
            endpointPort: 5092);
        commandRunner.Enqueue(new(0, "running", string.Empty));
        commandRunner.Enqueue(new(0, "running", string.Empty));
        commandRunner.Enqueue(new(1, string.Empty, "not found"));
        commandRunner.Enqueue(new(1, string.Empty, "not found"));
        commandRunner.EnqueueSuccess(3);
        commandRunner.Enqueue(new(0, "running", string.Empty));

        var diagnostics = await bridge.ApplyReplicasAsync(resource);

        Assert.Empty(diagnostics);
        Assert.Collection(
            commandRunner.Commands,
            command => AssertDockerInspect(command, "cloudshell-replicated-health-api-replica-1"),
            command => AssertDockerInspect(command, "cloudshell-replicated-health-api-replica-2"),
            command => AssertDockerInspect(command, "cloudshell-replicated-health-api-replica-3"),
            command => AssertDockerInspect(command, "cloudshell-replicated-health-api-replica-4"),
            AssertDockerNetworkCreate,
            command => AssertDockerRun(
                command,
                "cloudshell-replicated-health-api-replica-3",
                replica: 3,
                expectedProbePort: 5194,
                expectedReplicaCount: 4),
            command => AssertDockerRun(
                command,
                "cloudshell-replicated-health-api-replica-4",
                replica: 4,
                expectedProbePort: 5195,
                expectedReplicaCount: 4),
            command => AssertDockerInspect(command, LocalDockerContainerApplicationRuntimeConventions.CreateIngressContainerName()));
    }

    [Fact]
    public async Task RuntimeBridge_ApplyReplicasUpdatesIngressBeforeRemovingStaleReplicas()
    {
        var commandRunner = new RecordingCommandRunner();
        var bridge = CreateRuntimeBridge(
            commandRunner,
            CreateConfiguration(replicaCleanupLimit: 4));
        var resource = await CreateGraphAppResourceAsync(
            replicas: 2,
            endpointPort: 5092);
        commandRunner.Enqueue(new(0, "running", string.Empty));
        commandRunner.Enqueue(new(0, "running", string.Empty));
        commandRunner.Enqueue(new(0, "running", string.Empty));
        commandRunner.Enqueue(new(0, "running", string.Empty));
        commandRunner.EnqueueSuccess(1);
        commandRunner.Enqueue(new(0, "running", string.Empty));

        var diagnostics = await bridge.ApplyReplicasAsync(resource);

        Assert.Empty(diagnostics);
        Assert.Collection(
            commandRunner.Commands,
            command => AssertDockerInspect(command, "cloudshell-replicated-health-api-replica-1"),
            command => AssertDockerInspect(command, "cloudshell-replicated-health-api-replica-2"),
            command => AssertDockerInspect(command, "cloudshell-replicated-health-api-replica-3"),
            command => AssertDockerInspect(command, "cloudshell-replicated-health-api-replica-4"),
            AssertDockerNetworkCreate,
            command => AssertDockerInspect(command, LocalDockerContainerApplicationRuntimeConventions.CreateIngressContainerName()),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-api-replica-4"),
            command => AssertDockerRemove(command, "cloudshell-replicated-health-api-replica-3"));
    }

    [Fact]
    public async Task RuntimeBridge_OrchestratorPrimitivesStartReplicaAndReconcileIngressWithoutSweepingRuntime()
    {
        var commandRunner = new RecordingCommandRunner();
        var bridge = CreateRuntimeBridge(
            commandRunner,
            CreateConfiguration(replicaCleanupLimit: 4));
        var resource = await CreateGraphAppResourceAsync(
            replicas: 2,
            endpointPort: 5092);
        var service = CreateOrchestratorService(resource, replicas: 2);
        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(
            service,
            "rev-test");
        commandRunner.EnqueueSuccess(4);
        commandRunner.Enqueue(new(0, "running", string.Empty));

        var prepareDiagnostics = await bridge.PrepareOrchestratorServiceAsync(
            resource,
            service,
            replicaGroup,
            []);
        var startDiagnostics = await bridge.ExecuteOrchestratorServiceInstanceAsync(
            resource,
            service,
            replicaGroup.Instances[1],
            ResourceAction.Start,
            replicaGroup);
        var routingDiagnostics = await bridge.ReconcileOrchestratorServiceRoutingAsync(
            resource,
            service,
            replicaGroup,
            []);

        Assert.Empty(prepareDiagnostics);
        Assert.Empty(startDiagnostics);
        Assert.Empty(routingDiagnostics);
        Assert.Collection(
            commandRunner.Commands,
            command =>
            {
                Assert.Equal("dotnet", command.FileName);
                Assert.Equal("publish", command.Arguments[0]);
            },
            AssertDockerNetworkCreate,
            command => AssertDockerRemove(command, "cloudshell-replicated-health-api-replica-2"),
            command => AssertDockerRun(
                command,
                "cloudshell-replicated-health-api-replica-2",
                replica: 2,
                expectedProbePort: 5193,
                expectedRuntimeRevisionId: "rev-test"),
            command => AssertDockerInspect(command, LocalDockerContainerApplicationRuntimeConventions.CreateIngressContainerName()));
        Assert.DoesNotContain(
            commandRunner.Commands,
            command => command.Arguments.SequenceEqual(
                ["rm", "-f", LocalDockerContainerApplicationRuntimeConventions.CreateIngressContainerName()]));
        Assert.DoesNotContain(
            commandRunner.Commands,
            command => command.Arguments.SequenceEqual(
                ["rm", "-f", "cloudshell-replicated-health-api-replica-1"]));
    }

    [Fact]
    public async Task RuntimeBridge_RoutingReconciliationWritesTraefikStickyCookieConfiguration()
    {
        var contentRoot = Path.Combine(
            Path.GetTempPath(),
            $"cloudshell-replicated-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);
        try
        {
            var commandRunner = new RecordingCommandRunner();
            var bridge = CreateRuntimeBridge(
                commandRunner,
                CreateConfiguration(replicaCleanupLimit: 2),
                new TestHostEnvironment(contentRoot));
            var resource = await CreateGraphAppResourceAsync(
                replicas: 2,
                endpointPort: 5092);
            var service = CreateOrchestratorService(resource, replicas: 2);
            var replicaGroup = ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(
                service,
                "rev-test");
            var routingBinding = new ResourceOrchestratorServiceRoutingBindingDefinition(
                "api-http-routing",
                ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
                service.Name,
                replicaGroup.Id,
                ResourceEndpointReference.ForEndpoint(resource.EffectiveResourceId, "http"),
                SessionAffinity: ResourceOrchestratorSessionAffinityPolicy.Cookie(
                    "CloudShellReplica",
                    durationSeconds: 3600));
            commandRunner.Enqueue(new(0, "running", string.Empty));

            var diagnostics = await bridge.ReconcileOrchestratorServiceRoutingAsync(
                resource,
                service,
                replicaGroup,
                [routingBinding]);

            Assert.Empty(diagnostics);
            var configuration = await File.ReadAllTextAsync(
                Path.Combine(contentRoot, "Data", "runtime-ingress", "dynamic.yml"));
            Assert.Contains("sticky:", configuration);
            Assert.Contains("cookie:", configuration);
            Assert.Contains("name: \"CloudShellReplica\"", configuration);
            Assert.Contains("maxAge: 3600", configuration);
            Assert.Contains("cloudshell-replicated-health-api-replica-1:8080", configuration);
            Assert.Contains("cloudshell-replicated-health-api-replica-2:8080", configuration);
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RuntimeBridge_RoutingReconciliationUsesReplicaGroupInstancesForBackends()
    {
        var contentRoot = Path.Combine(
            Path.GetTempPath(),
            $"cloudshell-replicated-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);
        try
        {
            var commandRunner = new RecordingCommandRunner();
            var bridge = CreateRuntimeBridge(
                commandRunner,
                CreateConfiguration(replicaCleanupLimit: 4),
                new TestHostEnvironment(contentRoot));
            var resource = await CreateGraphAppResourceAsync(
                replicas: 4,
                endpointPort: 5092);
            var service = CreateOrchestratorService(resource, replicas: 4);
            var fullReplicaGroup = ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(
                service,
                "rev-test");
            var routedReplicaGroup = fullReplicaGroup with
            {
                RequestedReplicaSlots = 2,
                Instances = fullReplicaGroup.Instances
                    .Where(instance => instance.ReplicaOrdinal <= 2)
                    .ToArray()
            };
            var routingBinding = new ResourceOrchestratorServiceRoutingBindingDefinition(
                "api-http-routing",
                ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
                service.Name,
                routedReplicaGroup.Id,
                ResourceEndpointReference.ForEndpoint(resource.EffectiveResourceId, "http"));
            commandRunner.Enqueue(new(0, "running", string.Empty));

            var diagnostics = await bridge.ReconcileOrchestratorServiceRoutingAsync(
                resource,
                service,
                routedReplicaGroup,
                [routingBinding]);

            Assert.Empty(diagnostics);
            var configuration = await File.ReadAllTextAsync(
                Path.Combine(contentRoot, "Data", "runtime-ingress", "dynamic.yml"));
            Assert.Contains("cloudshell-replicated-health-api-replica-1:8080", configuration);
            Assert.Contains("cloudshell-replicated-health-api-replica-2:8080", configuration);
            Assert.DoesNotContain("cloudshell-replicated-health-api-replica-3:8080", configuration);
            Assert.DoesNotContain("cloudshell-replicated-health-api-replica-4:8080", configuration);
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RuntimeBridge_RoutingReconciliationUsesRoutingBindingEndpoint()
    {
        var contentRoot = Path.Combine(
            Path.GetTempPath(),
            $"cloudshell-replicated-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);
        try
        {
            var commandRunner = new RecordingCommandRunner();
            var bridge = CreateRuntimeBridge(
                commandRunner,
                CreateConfiguration(replicaCleanupLimit: 2),
                new TestHostEnvironment(contentRoot));
            var resource = await CreateGraphAppResourceAsync(
                replicas: 2,
                endpointRequests:
                [
                    new NetworkingEndpointRequestValue(
                        "public",
                        "http",
                        TargetPort: 8080,
                        Host: "localhost",
                        Port: 5092,
                        Exposure: "Local"),
                    new NetworkingEndpointRequestValue(
                        "admin",
                        "http",
                        TargetPort: 9090,
                        Host: "localhost",
                        Port: 6092,
                        Exposure: "Local")
                ]);
            var service = CreateOrchestratorService(resource, replicas: 2);
            var replicaGroup = ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(
                service,
                "rev-test");
            var routingBinding = new ResourceOrchestratorServiceRoutingBindingDefinition(
                "api-admin-routing",
                ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
                service.Name,
                replicaGroup.Id,
                ResourceEndpointReference.ForEndpoint(resource.EffectiveResourceId, "admin"));
            commandRunner.Enqueue(new(1, string.Empty, "missing"));
            commandRunner.EnqueueSuccess(1);

            var diagnostics = await bridge.ReconcileOrchestratorServiceRoutingAsync(
                resource,
                service,
                replicaGroup,
                [routingBinding]);

            Assert.Empty(diagnostics);
            var configuration = await File.ReadAllTextAsync(
                Path.Combine(contentRoot, "Data", "runtime-ingress", "dynamic.yml"));
            Assert.Contains("cloudshell-replicated-health-api-replica-1:9090", configuration);
            Assert.Contains("cloudshell-replicated-health-api-replica-2:9090", configuration);
            var ingressRun = Assert.Single(
                commandRunner.Commands,
                command => command.Arguments.FirstOrDefault() == "run" &&
                    command.Arguments.Contains(
                    LocalDockerContainerApplicationRuntimeConventions.CreateIngressContainerName()));
            Assert.Contains("127.0.0.1:6092:6092/tcp", ingressRun.Arguments);
            Assert.Contains("--entrypoints.http.address=:6092", ingressRun.Arguments);
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RuntimeBridge_ReplicaGroupStopDoesNotRemoveSlotReplacedByNewReplicaGroup()
    {
        var commandRunner = new RecordingCommandRunner();
        var bridge = CreateRuntimeBridge(
            commandRunner,
            CreateConfiguration(replicaCleanupLimit: 4));
        var resource = await CreateGraphAppResourceAsync(
            replicas: 2,
            endpointPort: 5092);
        var service = CreateOrchestratorService(resource, replicas: 2);
        var oldReplicaGroup = ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(
            service,
            "rev-old");
        var newReplicaGroup = ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(
            service,
            "rev-new");
        commandRunner.Enqueue(new(0, newReplicaGroup.Id, string.Empty));

        var diagnostics = await bridge.ExecuteOrchestratorServiceInstanceAsync(
            resource,
            service,
            oldReplicaGroup.Instances[0],
            ResourceAction.Stop,
            oldReplicaGroup);

        Assert.Empty(diagnostics);
        Assert.Collection(
            commandRunner.Commands,
            command => AssertDockerReplicaGroupLabelInspect(
                command,
                "cloudshell-replicated-health-api-replica-1"));
        Assert.DoesNotContain(
            commandRunner.Commands,
            command => command.Arguments.SequenceEqual(
                ["rm", "-f", "cloudshell-replicated-health-api-replica-1"]));
    }

    [Fact]
    public void RuntimeLogProvider_ProjectsReplicaLogSourcesFromGraphState()
    {
        var provider = new LocalContainerApplicationRuntimeLogProvider(
            new RecordingCommandRunner(),
            new RecordingResourceManagerStore(
                CreateResourceManagerGraphAppResource(replicas: 2),
                CreateGraphReplicaResource(replica: 1),
                CreateGraphReplicaResource(replica: 2)));
        var replica1 = CreateGraphReplicaResource(replica: 1);
        var replica2 = CreateGraphReplicaResource(replica: 2);

        var sources = provider.GetLogSources();

        Assert.Collection(
            sources,
            source =>
            {
                Assert.Equal("application.container-app:api:replica-1:logs", source.Id);
                Assert.Equal("Replica 1 logs", source.Name);
                Assert.Equal("application.container-app:api", source.ResourceId);
                Assert.Equal(replica1.Id, source.ProducerResourceId);
                Assert.Equal(ResourceLogSourceKind.Container, source.Kind);
                Assert.Equal(LogFormat.JsonConsole, source.Format);
            },
            source =>
            {
                Assert.Equal("application.container-app:api:replica-2:logs", source.Id);
                Assert.Equal("Replica 2 logs", source.Name);
                Assert.Equal("application.container-app:api", source.ResourceId);
                Assert.Equal(replica2.Id, source.ProducerResourceId);
                Assert.Equal(ResourceLogSourceKind.Container, source.Kind);
                Assert.Equal(LogFormat.JsonConsole, source.Format);
            });
    }

    [Fact]
    public async Task RuntimeLogProvider_ReadsReplicaContainerLogs()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(
            0,
            """
            2026-06-27T10:00:00.0000000Z {"message":"Handled demo work","severity":"Information","source":"ReplicatedApi","traceId":"trace-1","spanId":"span-1","state":{"path":"/work"}}

            """,
            string.Empty));
        var provider = new LocalContainerApplicationRuntimeLogProvider(
            commandRunner,
            new RecordingResourceManagerStore(
                CreateResourceManagerGraphAppResource(replicas: 2),
                CreateGraphReplicaResource(replica: 1),
                CreateGraphReplicaResource(replica: 2)));

        var entries = await provider.ReadLogSourceAsync(
            LocalContainerApplicationRuntimeLogProvider.CreateLogSourceId(CreateGraphReplicaResource(replica: 2)),
            maxEntries: 10);

        var command = Assert.Single(commandRunner.Commands);
        Assert.Equal("docker", command.FileName);
        Assert.Equal(
            [
                "logs",
                "--timestamps",
                "--tail",
                "10",
                "cloudshell-replicated-health-api-replica-2"
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
    public async Task RuntimeLogProvider_ReturnsRuntimeUnavailableDiagnosticEntry()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(
            LocalContainerApplicationCommandResult.UnavailableExitCode,
            string.Empty,
            "Docker executable 'docker' is unavailable."));
        var provider = new LocalContainerApplicationRuntimeLogProvider(
            commandRunner,
            new RecordingResourceManagerStore(
                CreateResourceManagerGraphAppResource(replicas: 1),
                CreateGraphReplicaResource(replica: 1)));

        var entries = await provider.ReadLogSourceAsync(
            LocalContainerApplicationRuntimeLogProvider.CreateLogSourceId(CreateGraphReplicaResource(replica: 1)),
            maxEntries: 10);

        var entry = Assert.Single(entries);
        Assert.Contains("Docker executable 'docker' is unavailable", entry.Message);
        Assert.Equal("cloudshell-replicated-health-api-replica-1", entry.Source);
    }

    [Fact]
    public async Task RuntimeMonitoringProvider_ReadsReplicaContainerStats()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(
            0,
            """
            {"Name":"cloudshell-replicated-health-api-replica-2","CPUPerc":"7.5%","MemUsage":"32MiB / 1GiB","NetIO":"2kB / 4kB","BlockIO":"1MiB / 2MiB","PIDs":"12"}
            """,
            string.Empty));
        var provider = new LocalContainerApplicationRuntimeMonitoringProvider(commandRunner);
        var replica = CreateGraphReplicaResource(replica: 2);

        Assert.True(provider.CanMonitor(replica));

        var snapshot = await provider.GetMonitoringSnapshotAsync(replica);

        var command = Assert.Single(commandRunner.Commands);
        Assert.Equal("docker", command.FileName);
        Assert.Equal(
            [
                "stats",
                "--no-stream",
                "--format",
                "{{json .}}",
                "cloudshell-replicated-health-api-replica-2"
            ],
            command.Arguments);
        Assert.False(command.ThrowOnError);
        Assert.NotNull(snapshot);
        Assert.Equal(replica.Id, snapshot.ResourceId);
        Assert.Equal("Available", snapshot.Status);
        Assert.Contains(snapshot.Metrics, metric =>
            metric.Name == "resource.cpu.usage" &&
            metric.Value == 7.5);
        Assert.Contains(snapshot.Metrics, metric =>
            metric.Name == "resource.process.count" &&
            metric.Value == 12);
    }

    [Fact]
    public async Task RuntimeMonitoringProvider_ReportsRuntimeUnavailableReason()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(
            LocalContainerApplicationCommandResult.UnavailableExitCode,
            string.Empty,
            "Docker executable 'docker' is unavailable."));
        var provider = new LocalContainerApplicationRuntimeMonitoringProvider(commandRunner);
        var replica = CreateGraphReplicaResource(replica: 2);

        var snapshot = await provider.GetMonitoringSnapshotAsync(replica);

        Assert.NotNull(snapshot);
        Assert.Equal("Unavailable", snapshot.Status);
        Assert.Empty(snapshot.Metrics);
        Assert.Equal(
            "The container runtime could not read stats for runtime replica 'api replica 2' (container 'cloudshell-replicated-health-api-replica-2'): Docker executable 'docker' is unavailable.",
            snapshot.Message);
    }

    [Fact]
    public async Task RuntimeMonitoringProvider_ReportsReplicaContextWhenStatsCannotBeParsed()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(
            0,
            "not-json",
            string.Empty));
        var provider = new LocalContainerApplicationRuntimeMonitoringProvider(commandRunner);
        var replica = CreateGraphReplicaResource(replica: 1);

        var snapshot = await provider.GetMonitoringSnapshotAsync(replica);

        Assert.NotNull(snapshot);
        Assert.Equal("Unavailable", snapshot.Status);
        Assert.Empty(snapshot.Metrics);
        Assert.Equal(
            "The container runtime did not return a stats snapshot for runtime replica 'api replica 1' (container 'cloudshell-replicated-health-api-replica-1').",
            snapshot.Message);
    }

    [Fact]
    public async Task ReplicaSlotMaterializationProvider_IgnoresUnavailableContainerRuntime()
    {
        var commandRunner = new RecordingCommandRunner();
        commandRunner.Enqueue(new(
            LocalContainerApplicationCommandResult.UnavailableExitCode,
            string.Empty,
            "Docker executable 'docker' is unavailable."));
        commandRunner.Enqueue(new(
            LocalContainerApplicationCommandResult.UnavailableExitCode,
            string.Empty,
            "Docker executable 'docker' is unavailable."));
        var provider = new LocalDockerContainerApplicationReplicaSlotMaterializationProvider(
            commandRunner,
            Options.Create(CreateRuntimeOptions()));
        var resource = CreateResourceManagerGraphAppResource(replicas: 2);
        var graphResource = await CreateGraphAppResourceAsync(replicas: 2);
        var service = CreateOrchestratorService(graphResource, replicas: 2);
        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(
            service,
            "rev-test");

        var slots = await provider.GetMaterializedReplicaSlotsAsync(resource, replicaGroup);

        Assert.Empty(slots);
        Assert.Collection(
            commandRunner.Commands,
            command => AssertDockerInspect(command, "cloudshell-replicated-health-api-replica-1"),
            command => AssertDockerInspect(command, "cloudshell-replicated-health-api-replica-2"));
    }

    [Fact]
    public async Task Handler_DelegatesMappedGraphApiToBridge()
    {
        var bridge = new RecordingContainerAppRuntimeBridge(ContainerApplicationRuntimeStatus.Running);
        var handler = CreateHandler(bridge);
        var resource = await CreateGraphAppResourceAsync();

        Assert.Equal(ContainerApplicationRuntimeStatus.Running, handler.GetStatus(resource));

        var diagnostics = await handler.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        var command = Assert.Single(bridge.LifecycleCommands);
        Assert.Equal("application.container-app:api", command.Resource.EffectiveResourceId);
        Assert.Equal(ContainerApplicationResourceTypeProvider.Operations.Start, command.OperationId);
    }

    [Fact]
    public async Task Handler_DelegatesMappedGraphApiRoutingTearDownToBridge()
    {
        var bridge = new RecordingContainerAppRuntimeBridge(ContainerApplicationRuntimeStatus.Running);
        var handler = CreateHandler(bridge);
        var resource = await CreateGraphAppResourceAsync();
        var service = CreateOrchestratorService(resource, replicas: 2);
        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(
            service,
            "rev-test");

        var diagnostics = await handler.TearDownOrchestratorServiceRoutingAsync(
            resource,
            service,
            replicaGroup,
            []);

        Assert.Empty(diagnostics);
        var command = Assert.Single(bridge.OrchestratorCommands);
        Assert.Equal("routing-teardown", command.Stage);
        Assert.Equal("application.container-app:api", command.Resource.EffectiveResourceId);
    }

    [Fact]
    public async Task Handler_IgnoresUnmappedContainerAppWithoutCallingBridge()
    {
        var bridge = new RecordingContainerAppRuntimeBridge(ContainerApplicationRuntimeStatus.Running);
        var handler = CreateHandler(bridge);
        var resource = await CreateGraphAppResourceAsync(
            name: "other",
            resourceId: "application.container-app:other");

        var diagnostics = await handler.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Start);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(NoopContainerApplicationRuntimeHandler.RuntimeUnavailableDiagnosticCode, diagnostic.Code);
        Assert.Empty(bridge.LifecycleCommands);
        Assert.Equal(ContainerApplicationRuntimeStatus.Unknown, handler.GetStatus(resource));
    }

    [Fact]
    public async Task RuntimeDescriptorProvider_MarksGraphApiAsControlPlaneScopedRuntimeWorkload()
    {
        var provider = new LocalDockerContainerApplicationOrchestrationDescriptorProvider();
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
        Assert.Equal("api", workload.Name);
    }

    private static async Task<GraphResource> CreateGraphAppResourceAsync(
        string name = "api",
        string resourceId = "application.container-app:api",
        string image = "cloudshell-application-api:20260622.2",
        int replicas = 3,
        int? endpointPort = null,
        bool includeHealthChecks = false,
        bool includeCookieSessionAffinity = false,
        IReadOnlyList<NetworkingEndpointRequestValue>? endpointRequests = null,
        string? projectPath = null,
        string? containerBuildContext = null,
        string? javaProjectPath = null,
        string? javaBuildTool = null,
        string? javaBuildArguments = null)
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
        if (endpointRequests is not null)
        {
            attributes[ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests] =
                ResourceAttributeValue.FromObject(endpointRequests);
        }
        else if (endpointPort is not null)
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

        if (includeCookieSessionAffinity)
        {
            attributes[ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityMode] =
                ResourceOrchestratorSessionAffinityMode.Cookie.ToString();
            attributes[ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityCookieName] =
                "CloudShellReplica";
            attributes[ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityDurationSeconds] =
                3600;
        }

        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            attributes[ResourceAttributeId.Create(ResourceAttributeNames.ProjectPath)] = projectPath;
        }

        if (!string.IsNullOrWhiteSpace(containerBuildContext))
        {
            attributes[ContainerApplicationResourceTypeProvider.Attributes.ContainerBuildContext] =
                containerBuildContext;
        }

        if (!string.IsNullOrWhiteSpace(javaProjectPath))
        {
            attributes[JavaAppResourceTypeProvider.Attributes.ProjectPath] = javaProjectPath;
        }

        if (!string.IsNullOrWhiteSpace(javaBuildTool))
        {
            attributes[JavaAppResourceTypeProvider.Attributes.BuildTool] = javaBuildTool;
        }

        if (!string.IsNullOrWhiteSpace(javaBuildArguments))
        {
            attributes[JavaAppResourceTypeProvider.Attributes.BuildArguments] = javaBuildArguments;
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

    private static DelegatingContainerApplicationRuntimeHandler CreateHandler(
        ILocalDockerContainerApplicationRuntimeBridge bridge) =>
        new([new LocalDockerContainerApplicationRuntimeTarget(bridge)]);

    private static LocalDockerContainerApplicationRuntimeBridge CreateRuntimeBridge(
        RecordingCommandRunner commandRunner,
        IConfiguration configuration,
        IHostEnvironment? hostEnvironment = null,
        LocalDockerContainerApplicationRuntimeOptions? options = null) =>
        new(
            commandRunner,
            Options.Create(options ?? CreateRuntimeOptions(contentRootRelative: hostEnvironment is not null)),
            configuration,
            hostEnvironment);

    private static LocalDockerContainerApplicationRuntimeOptions CreateRuntimeOptions(
        bool contentRootRelative = false)
    {
        var options = new LocalDockerContainerApplicationRuntimeOptions();
        options.AddApplication(
            LocalDockerContainerApplicationRuntimeConventions.ApiResourceId,
            contentRootRelative
                ? Path.Combine("Api", "CloudShell.ReplicatedContainerHealth.Api.csproj")
                : LocalDockerContainerApplicationRuntimeConventions.ReplicatedContainerHealthDefaults.ProjectPath,
            definition =>
            {
                var defaults = LocalDockerContainerApplicationRuntimeConventions.ReplicatedContainerHealthDefaults;
                definition.IngressContainerName = defaults.IngressContainerName;
                definition.IngressConfigurationDirectory = contentRootRelative
                    ? Path.Combine("Data", "runtime-ingress")
                    : defaults.IngressConfigurationDirectory;
                definition.ReplicaContainerNamePrefix = defaults.ReplicaContainerNamePrefix;
                definition.ReplicaNetworkAliasPrefix = defaults.ReplicaNetworkAliasPrefix;
                definition.ReplicaResourceIdPrefix = defaults.ReplicaResourceIdPrefix;
                definition.ReplicaServiceNamePrefix = defaults.ReplicaServiceNamePrefix;
                definition.RuntimeResourceProviderId = defaults.RuntimeResourceProviderId;
                definition.RuntimeResourceProviderName = defaults.RuntimeResourceProviderName;
                definition.RuntimeMaterialization = defaults.RuntimeMaterialization;
            });
        return options;
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
            "application.container-app:api",
            "api",
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
            },
            LogSources:
            [
                new(
                    "console",
                    "Console logs",
                    ResourceLogSourceKind.Container,
                    LogFormat.JsonConsole,
                    Purpose: ResourceLogSourcePurpose.Default)
            ]);

    private static CloudShell.Abstractions.ResourceManager.Resource CreateGraphReplicaResource(
        int replica)
    {
        var replicaOrdinal = replica.ToString(CultureInfo.InvariantCulture);

        return new CloudShell.Abstractions.ResourceManager.Resource(
            LocalDockerContainerApplicationRuntimeConventions.CreateReplicaResourceId(replica),
            $"api replica {replicaOrdinal}",
            "Container replica",
            "Replicated Container Health",
            "local",
            CloudShell.Abstractions.ResourceManager.ResourceState.Running,
            [],
            "v1",
            DateTimeOffset.UtcNow,
            [],
            ParentResourceId: LocalDockerContainerApplicationRuntimeConventions.ApiResourceId,
            TypeId: "runtime.container",
            ResourceClass: CloudShell.Abstractions.ResourceManager.ResourceClass.Container,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.RuntimeKind] = "containerReplica",
                [ResourceAttributeNames.RuntimeContainerName] =
                    LocalDockerContainerApplicationRuntimeConventions.CreateReplicaContainerName(replica),
                [ResourceAttributeNames.RuntimeReplicaOrdinal] = replicaOrdinal,
                [ResourceAttributeNames.RuntimeReplicaCount] = "3",
                [ResourceAttributeNames.RuntimeMaterialization] = "sampleRuntime"
            },
            Capabilities:
            [
                new(ResourceCapabilityIds.Monitoring),
                new(ResourceCapabilityIds.LogSources)
            ],
            Source: ResourceSource.Orchestrator,
            ManagementMode: ResourceManagementMode.RuntimeManaged,
            Visibility: ResourceVisibility.Hidden,
            OwnerResourceId: LocalDockerContainerApplicationRuntimeConventions.ApiResourceId,
            CleanupBehavior: ResourceCleanupBehavior.DeleteWithOwner);
    }

    private static IConfiguration CreateConfiguration(
        int? replicaCleanupLimit = null,
        int? statusCacheMilliseconds = null,
        string? urls = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Observability:TraceIngestEndpoint"] = "http://host.docker.internal:5011/api/control-plane/v1/traces/ingest",
            ["Observability:MetricIngestEndpoint"] = "http://host.docker.internal:5011/api/control-plane/v1/metrics/ingest"
        };
        if (!string.IsNullOrWhiteSpace(urls))
        {
            values["urls"] = urls;
        }

        if (replicaCleanupLimit is not null)
        {
            values["ReplicatedContainerHealth:RuntimeReplicaCleanupLimit"] = replicaCleanupLimit.Value.ToString();
        }

        if (statusCacheMilliseconds is not null)
        {
            values["ReplicatedContainerHealth:RuntimeStatusCacheMilliseconds"] =
                statusCacheMilliseconds.Value.ToString();
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static ResourceOrchestratorService CreateOrchestratorService(
        GraphResource resource,
        int replicas) =>
        new(
            resource.EffectiveResourceId,
            "cloudshell-application-container-app-api",
            new ResourceWorkloadConfiguration(
                ResourceWorkloadKind.ContainerImage,
                "api",
                Image: resource.Attributes.GetString(ContainerApplicationResourceTypeProvider.Attributes.ContainerImage),
                Replicas: replicas,
                ReplicasEnabled: replicas > 1));

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

    private static void AssertDockerReplicaGroupLabelInspect(
        RecordingCommand command,
        string containerName)
    {
        Assert.Equal("docker", command.FileName);
        Assert.Equal(
            [
                "container",
                "inspect",
                "--format",
                "{{ index .Config.Labels \"cloudshell.replica-group-id\" }}",
                containerName
            ],
            command.Arguments);
        Assert.False(command.ThrowOnError);
    }

    private static void AssertDockerNetworkCreate(RecordingCommand command)
    {
        Assert.Equal("docker", command.FileName);
        Assert.Equal(["network", "create", "cloudshell"], command.Arguments);
        Assert.False(command.ThrowOnError);
    }

    private static void AssertCommandOption(
        RecordingCommand command,
        string option,
        string expectedValue) =>
        Assert.Equal(expectedValue, GetCommandOption(command, option));

    private static string? GetCommandOption(
        RecordingCommand command,
        string option)
    {
        for (var index = 0; index < command.Arguments.Count - 1; index++)
        {
            if (string.Equals(command.Arguments[index], option, StringComparison.Ordinal))
            {
                return command.Arguments[index + 1];
            }
        }

        return null;
    }

    private static void AssertDockerRun(
        RecordingCommand command,
        string containerName,
        int replica,
        int? expectedProbePort = null,
        int expectedReplicaCount = 2,
        string? expectedRuntimeRevisionId = null)
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
            LocalDockerContainerApplicationRuntimeConventions.CreateReplicaNetworkAlias(replica),
            command.Arguments);
        Assert.Contains($"CLOUDSHELL_REPLICA_ORDINAL={replica}", command.Arguments);
        Assert.Contains(
            $"CLOUDSHELL_RESOURCE_ID={LocalDockerContainerApplicationRuntimeConventions.CreateReplicaResourceId(replica)}",
            command.Arguments);
        Assert.Contains(
            $"CLOUDSHELL_TELEMETRY_RESOURCE_ID={LocalDockerContainerApplicationRuntimeConventions.ApiResourceId}",
            command.Arguments);
        Assert.Contains(
            $"OTEL_SERVICE_NAME=replicated-container-health-api-replica-{replica.ToString(CultureInfo.InvariantCulture)}",
            command.Arguments);
        var otelAttributes = Assert.Single(command.Arguments, argument =>
            argument.StartsWith("OTEL_RESOURCE_ATTRIBUTES=", StringComparison.Ordinal));
        foreach (var attribute in CreateExpectedOtelResourceAttributes(
                     replica,
                     expectedReplicaCount,
                     expectedRuntimeRevisionId))
        {
            Assert.Contains(attribute, otelAttributes);
        }
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
        Assert.Contains(LocalDockerContainerApplicationRuntimeConventions.CreateIngressContainerName(), command.Arguments);
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

    private static IReadOnlyList<string> CreateExpectedOtelResourceAttributes(
        int replica,
        int replicaCount,
        string? runtimeRevisionId = null)
    {
        var resourceId = LocalDockerContainerApplicationRuntimeConventions.CreateReplicaResourceId(replica);
        var containerName = LocalDockerContainerApplicationRuntimeConventions.CreateReplicaContainerName(replica);
        var expectedRevisionId = runtimeRevisionId ?? ContainerApplicationRuntimeRevisions.CreateImageRevisionId(
            ContainerRegistryDefaults.Default,
            "cloudshell-application-api:20260622.2");
        return
        [
            $"service.instance.id={resourceId}",
            $"cloudshell.resource.id={resourceId}",
            "cloudshell.resource.type=runtime.container",
            $"telemetry.scope.resourceId={LocalDockerContainerApplicationRuntimeConventions.ApiResourceId}",
            $"telemetry.scope.name=Replica {replica.ToString(CultureInfo.InvariantCulture)}",
            "telemetry.scope.kind=runtime",
            $"runtime.replica.ordinal={replica.ToString(CultureInfo.InvariantCulture)}",
            $"runtime.replica.count={replicaCount.ToString(CultureInfo.InvariantCulture)}",
            $"runtime.container.name={containerName}",
            $"deployment.revision={expectedRevisionId}"
        ];
    }

    private static void AssertGraphReplicaResource(
        CloudShell.Abstractions.ResourceManager.Resource replica,
        int ordinal,
        int port,
        string expectedServiceId,
        string expectedReplicaGroupId,
        string expectedRevisionId)
    {
        Assert.Equal(
            LocalDockerContainerApplicationRuntimeConventions.CreateReplicaResourceId(ordinal),
            replica.Id);
        Assert.Equal(LocalDockerContainerApplicationRuntimeConventions.ApiResourceId, replica.ParentResourceId);
        Assert.Equal(ResourceManagementMode.RuntimeManaged, replica.ManagementMode);
        Assert.Equal(ResourceVisibility.Hidden, replica.Visibility);
        Assert.Equal(CloudShell.Abstractions.ResourceManager.ResourceState.Running, replica.State);
        Assert.Equal("runtime.container", replica.TypeId);
        Assert.Equal(expectedServiceId, replica.ResourceAttributes[ResourceAttributeNames.DeploymentServiceId]);
        Assert.Equal(expectedReplicaGroupId, replica.ResourceAttributes[ResourceAttributeNames.DeploymentReplicaGroupId]);
        Assert.Equal("containerReplica", replica.ResourceAttributes[ResourceAttributeNames.RuntimeKind]);
        Assert.Equal(
            LocalDockerContainerApplicationRuntimeConventions.CreateReplicaContainerName(ordinal),
            replica.ResourceAttributes[ResourceAttributeNames.RuntimeContainerName]);
        Assert.Equal(
            LocalDockerContainerApplicationRuntimeConventions.CreateReplicaNetworkAlias(ordinal),
            replica.ResourceAttributes[ResourceAttributeNames.RuntimeNetworkAlias]);
        Assert.Equal(ordinal.ToString(CultureInfo.InvariantCulture), replica.ResourceAttributes[ResourceAttributeNames.RuntimeReplicaOrdinal]);
        Assert.Equal("2", replica.ResourceAttributes[ResourceAttributeNames.RuntimeReplicaCount]);
        Assert.Equal(expectedRevisionId, replica.ResourceAttributes[ResourceAttributeNames.RuntimeRevision]);
        Assert.Equal(2, replica.ResourceHealthChecks.Count);
        Assert.Contains(replica.ResourceHealthChecks, check => check.Type == ResourceProbeType.Health);
        Assert.Contains(replica.ResourceHealthChecks, check => check.Type == ResourceProbeType.Liveness);
        Assert.True(replica.EffectiveObservability.Logs);
        Assert.True(replica.EffectiveObservability.Traces);
        Assert.True(replica.EffectiveObservability.Metrics);
        Assert.True(replica.HasCapability(ResourceCapabilityIds.Monitoring));
        Assert.Equal(
            $"replicated-container-health-api-replica-{ordinal.ToString(CultureInfo.InvariantCulture)}",
            replica.EffectiveObservability.ServiceName);
        Assert.Equal(
            LocalDockerContainerApplicationRuntimeConventions.ApiResourceId,
            replica.EffectiveObservability.Attributes["telemetry.scope.resourceId"]);
        Assert.Equal(
            ordinal.ToString(CultureInfo.InvariantCulture),
            replica.EffectiveObservability.Attributes["runtime.replica.ordinal"]);
        Assert.Equal(
            expectedRevisionId,
            replica.EffectiveObservability.Attributes[TelemetryAttributeNames.DeploymentRevision]);
        var scope = Assert.Single(replica.EffectiveObservability.TelemetryScopes);
        Assert.Equal(LocalDockerContainerApplicationRuntimeConventions.ApiResourceId, scope.ScopeResourceId);
        Assert.Equal($"Replica {ordinal.ToString(CultureInfo.InvariantCulture)}", scope.Name);
        Assert.Equal("runtime", scope.Kind);
        Assert.Equal(expectedRevisionId, scope.DeploymentRevision);
        var mapping = Assert.Single(replica.ResourceEndpointNetworkMappings);
        Assert.Equal($"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}", mapping.Address);
    }

    private sealed class RecordingContainerAppRuntimeBridge(
        ContainerApplicationRuntimeStatus status) : ILocalDockerContainerApplicationRuntimeBridge
    {
        public List<LifecycleCommand> LifecycleCommands { get; } = [];
        public List<OrchestratorCommand> OrchestratorCommands { get; } = [];

        public bool CanHandle(GraphResource resource) =>
            string.Equals(
                resource.EffectiveResourceId,
                LocalDockerContainerApplicationRuntimeConventions.ApiResourceId,
                StringComparison.OrdinalIgnoreCase);

        public bool TryResolveDefinition(
            GraphResource resource,
            out LocalDockerContainerApplicationRuntimeDefinition definition)
        {
            if (CanHandle(resource))
            {
                definition = LocalDockerContainerApplicationRuntimeConventions.ReplicatedContainerHealthDefaults;
                return true;
            }

            definition = null!;
            return false;
        }

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

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> PrepareOrchestratorServiceAsync(
            GraphResource resource,
            ResourceOrchestratorService service,
            ResourceOrchestratorReplicaGroup? replicaGroup,
            IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
            CancellationToken cancellationToken = default)
        {
            OrchestratorCommands.Add(new("prepare", resource, null, null, routingBindings.Count));
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileOrchestratorServiceRoutingAsync(
            GraphResource resource,
            ResourceOrchestratorService service,
            ResourceOrchestratorReplicaGroup? replicaGroup,
            IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
            CancellationToken cancellationToken = default)
        {
            OrchestratorCommands.Add(new("routing", resource, null, null, routingBindings.Count));
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> TearDownOrchestratorServiceRoutingAsync(
            GraphResource resource,
            ResourceOrchestratorService service,
            ResourceOrchestratorReplicaGroup? replicaGroup,
            IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
            CancellationToken cancellationToken = default)
        {
            OrchestratorCommands.Add(new("routing-teardown", resource, null, null, routingBindings.Count));
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteOrchestratorServiceInstanceAsync(
            GraphResource resource,
            ResourceOrchestratorService service,
            ResourceOrchestratorServiceInstance instance,
            ResourceAction action,
            ResourceOrchestratorReplicaGroup? replicaGroup,
            CancellationToken cancellationToken = default)
        {
            OrchestratorCommands.Add(new("instance", resource, instance.ReplicaOrdinal, action.Kind, 0));
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed record LifecycleCommand(
        GraphResource Resource,
        ResourceOperationId OperationId);

    private sealed record OrchestratorCommand(
        string Stage,
        GraphResource Resource,
        int? ReplicaOrdinal,
        ResourceActionKind? ActionKind,
        int RoutingBindingCount);

    private sealed class RecordingResourceManagerStore(
        params CloudShell.Abstractions.ResourceManager.Resource[] resources) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => [];

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public IReadOnlyList<CloudShell.Abstractions.ResourceManager.Resource> GetAvailableResources() => resources;

        public IReadOnlyList<CloudShell.Abstractions.ResourceManager.Resource> GetResources() => resources;

        public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() => [];

        public CloudShell.Abstractions.ResourceManager.ResourceClass? GetResourceTypeClass(string resourceType) =>
            CloudShell.Abstractions.ResourceManager.ResourceClass.Container;

        public CloudShell.Abstractions.ResourceManager.Resource? GetResource(string id) =>
            resources.FirstOrDefault(resource =>
                string.Equals(id, resource.Id, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<CloudShell.Abstractions.ResourceManager.Resource> GetChildren(string resourceId) => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) => true;
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.Sample.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RecordingCommandRunner : ILocalContainerApplicationCommandRunner
    {
        private readonly Queue<LocalContainerApplicationCommandResult> _results = [];

        public List<RecordingCommand> Commands { get; } = [];

        public void Enqueue(LocalContainerApplicationCommandResult result) =>
            _results.Enqueue(result);

        public void EnqueueSuccess(int count)
        {
            for (var index = 0; index < count; index++)
            {
                Enqueue(new(0, string.Empty, string.Empty));
            }
        }

        public LocalContainerApplicationCommandResult Run(
            string fileName,
            IReadOnlyList<string> arguments,
            bool throwOnError = true,
            TimeSpan? timeout = null,
            string? workingDirectory = null) =>
            RunCore(fileName, arguments, throwOnError, timeout, workingDirectory);

        public Task<LocalContainerApplicationCommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken,
            bool throwOnError = true,
            TimeSpan? timeout = null,
            string? workingDirectory = null)
        {
            return Task.FromResult(RunCore(fileName, arguments, throwOnError, timeout, workingDirectory));
        }

        private LocalContainerApplicationCommandResult RunCore(
            string fileName,
            IReadOnlyList<string> arguments,
            bool throwOnError,
            TimeSpan? timeout,
            string? workingDirectory)
        {
            Commands.Add(new(fileName, arguments.ToArray(), throwOnError, timeout, workingDirectory));
            var result = _results.Count == 0
                ? new LocalContainerApplicationCommandResult(0, string.Empty, string.Empty)
                : _results.Dequeue();
            if (throwOnError &&
                result.ExitCode is not 0 and not LocalContainerApplicationCommandResult.TimeoutExitCode)
            {
                throw new InvalidOperationException(result.Error);
            }

            return result;
        }
    }

    private sealed record RecordingCommand(
        string FileName,
        IReadOnlyList<string> Arguments,
        bool ThrowOnError,
        TimeSpan? Timeout,
        string? WorkingDirectory);
}
