using System.Text.Json;
using CloudShell.ControlPlane.Providers;
using CloudShell.Providers.Traefik;
using CloudShell.ResourceModel;
using Microsoft.Extensions.DependencyInjection;
using GraphResource = CloudShell.ResourceModel.Resource;
using RMResourceAction = global::CloudShell.Abstractions.ResourceManager.ResourceAction;
using RMResourceOrchestratorReplicaGroup = global::CloudShell.Abstractions.ResourceManager.ResourceOrchestratorReplicaGroup;
using RMResourceOrchestratorReplicaGroups = global::CloudShell.Abstractions.ResourceManager.ResourceOrchestratorReplicaGroups;
using RMResourceOrchestratorService = global::CloudShell.Abstractions.ResourceManager.ResourceOrchestratorService;
using RMResourceOrchestratorServiceInstance = global::CloudShell.Abstractions.ResourceManager.ResourceOrchestratorServiceInstance;
using RMResourceOrchestratorServiceRoutingBindingDefinition = global::CloudShell.Abstractions.ResourceManager.ResourceOrchestratorServiceRoutingBindingDefinition;
using RMResourceWorkloadConfiguration = global::CloudShell.Abstractions.ResourceManager.ResourceWorkloadConfiguration;
using RMResourceWorkloadKind = global::CloudShell.Abstractions.ResourceManager.ResourceWorkloadKind;

namespace CloudShell.ControlPlane.Tests;

public sealed class ProviderExecutionContractTests
{
    [Fact]
    public void Request_CapturesAssignmentShapedExecutionWithoutAgentTransport()
    {
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ContainerStart,
            TargetResourceId = "container-app:orders",
            DesiredGeneration = 42,
            IdempotencyKey = "container-app:orders:42",
            RequiredCapabilities =
            [
                ProviderExecutionCapabilities.Containers,
                ProviderExecutionCapabilities.VolumeMounts
            ],
            Metadata = new Dictionary<string, string>
            {
                ["provider"] = "local-docker"
            },
            RequestedAt = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero)
        };

        Assert.Equal("assignment-1", request.AssignmentId);
        Assert.Equal(ProviderExecutionInstructionTypes.ContainerStart, request.InstructionType);
        Assert.Equal("container-app:orders", request.TargetResourceId);
        Assert.Equal(42, request.DesiredGeneration);
        Assert.Equal("container-app:orders:42", request.IdempotencyKey);
        Assert.Equal(ProviderExecutionTargetKind.Default, request.Target.Kind);
        Assert.Contains(ProviderExecutionCapabilities.Containers, request.RequiredCapabilities);
        Assert.Contains(ProviderExecutionCapabilities.VolumeMounts, request.RequiredCapabilities);
        Assert.Equal("local-docker", request.Metadata["provider"]);
    }

    [Fact]
    public void Requests_CreateForResource_CreatesResourceScopedExecutionIdentity()
    {
        var requestedAt = new DateTimeOffset(2026, 7, 14, 12, 15, 0, TimeSpan.Zero);
        var resource = CreateGraphResource("container-app:orders", "orders", revision: 12);
        var dependency = CreateGraphResource("volume:data", "data");
        var payload = JsonSerializer.SerializeToElement(new { image = "orders:42" });
        var target = new ProviderExecutionTarget
        {
            Kind = ProviderExecutionTargetKind.Agent,
            TargetId = "agent-a",
            Metadata = new Dictionary<string, string>
            {
                ["region"] = "local"
            }
        };
        var metadata = new Dictionary<string, string>
        {
            ["source"] = "test"
        };

        var request = ProviderExecutionRequests.CreateForResource(
            resource,
            "image.apply",
            ProviderExecutionInstructionTypes.ContainerApplicationImageApply,
            [ProviderExecutionCapabilities.Containers],
            [resource, dependency],
            payload,
            target,
            metadata,
            requestedAt: requestedAt);

        Assert.Equal("container-app:orders:image.apply", request.AssignmentId);
        Assert.Equal(
            ProviderExecutionInstructionTypes.ContainerApplicationImageApply,
            request.InstructionType);
        Assert.Equal(resource.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(12, request.DesiredGeneration);
        Assert.Equal("container-app:orders:image.apply:12", request.IdempotencyKey);
        Assert.Same(target, request.Target);
        Assert.Equal([ProviderExecutionCapabilities.Containers], request.RequiredCapabilities);
        Assert.Same(metadata, request.Metadata);
        Assert.Same(resource, request.TargetResourceSnapshot);
        Assert.Equal([resource, dependency], request.ResourceSnapshot);
        Assert.Equal(payload.GetRawText(), request.Payload?.GetRawText());
        Assert.Equal(requestedAt, request.RequestedAt);
    }

    [Fact]
    public void Succeeded_CorrelatesObservedGenerationWithRequestedAssignment()
    {
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.NetworkEndpointReconcile,
            TargetResourceId = "network:private",
            DesiredGeneration = 7,
            IdempotencyKey = "network:private:7"
        };

        var observedAt = new DateTimeOffset(2026, 7, 14, 12, 5, 0, TimeSpan.Zero);
        var result = ProviderExecutionResult.Succeeded(
            request,
            observations: new Dictionary<string, string>
            {
                ["endpointMappings"] = "3"
            },
            observedAt: observedAt);

        Assert.Equal(request.AssignmentId, result.AssignmentId);
        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal(request.DesiredGeneration, result.ObservedGeneration);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("3", result.Observations["endpointMappings"]);
        Assert.Equal(observedAt, result.ObservedAt);
    }

    [Fact]
    public async Task InProcessDispatcher_RoutesRequestToMatchingCapableHandler()
    {
        var handler = new RecordingExecutionHandler(
            ProviderExecutionInstructionTypes.ProcessStart,
            [ProviderExecutionCapabilities.Processes]);
        var dispatcher = new InProcessProviderExecutionDispatcher([handler]);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ProcessStart,
            TargetResourceId = "application:worker",
            DesiredGeneration = 3,
            IdempotencyKey = "application:worker:3",
            Target = ProviderExecutionTarget.InProcess,
            RequiredCapabilities = [ProviderExecutionCapabilities.Processes]
        };

        var result = await dispatcher.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Same(request, handler.Request);
    }

    [Fact]
    public async Task InProcessDispatcher_RecordsSuccessfulExecutionObservation()
    {
        var handler = new RecordingExecutionHandler(
            ProviderExecutionInstructionTypes.ProcessStart,
            [ProviderExecutionCapabilities.Processes]);
        var observations = new InMemoryProviderExecutionObservationStore();
        var dispatcher = new InProcessProviderExecutionDispatcher([handler], observations);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ProcessStart,
            TargetResourceId = "application:worker",
            DesiredGeneration = 3,
            IdempotencyKey = "application:worker:3",
            Target = ProviderExecutionTarget.InProcess,
            RequiredCapabilities = [ProviderExecutionCapabilities.Processes],
            RequestedAt = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero)
        };

        await dispatcher.ExecuteAsync(request);

        var observation = await observations.GetAsync(request.AssignmentId);

        Assert.NotNull(observation);
        Assert.Equal(request.AssignmentId, observation.AssignmentId);
        Assert.Equal(request.InstructionType, observation.InstructionType);
        Assert.Equal(request.TargetResourceId, observation.TargetResourceId);
        Assert.Equal(request.DesiredGeneration, observation.DesiredGeneration);
        Assert.Equal(request.IdempotencyKey, observation.IdempotencyKey);
        Assert.Equal(request.Target, observation.Target);
        Assert.Equal(request.RequiredCapabilities, observation.RequiredCapabilities);
        Assert.Equal(request.RequestedAt, observation.RequestedAt);
        Assert.Equal(ProviderExecutionStatus.Succeeded, observation.Status);
        Assert.Equal(request.DesiredGeneration, observation.ObservedGeneration);
        Assert.Empty(observation.Diagnostics);
        Assert.NotEqual(default, observation.RecordedAt);
    }

    [Fact]
    public async Task InProcessDispatcher_ReturnsFailedWhenHandlerReturnsDifferentAssignment()
    {
        var handler = new MismatchedAssignmentExecutionHandler(
            ProviderExecutionInstructionTypes.ProcessStart,
            [ProviderExecutionCapabilities.Processes],
            "assignment-2");
        var observations = new InMemoryProviderExecutionObservationStore();
        var dispatcher = new InProcessProviderExecutionDispatcher([handler], observations);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ProcessStart,
            TargetResourceId = "application:worker",
            DesiredGeneration = 3,
            IdempotencyKey = "application:worker:3",
            Target = ProviderExecutionTarget.InProcess,
            RequiredCapabilities = [ProviderExecutionCapabilities.Processes]
        };

        var result = await dispatcher.ExecuteAsync(request);
        var observation = await observations.GetAsync(request.AssignmentId);

        Assert.Equal(request.AssignmentId, result.AssignmentId);
        Assert.Equal(ProviderExecutionStatus.Failed, result.Status);
        Assert.Null(result.ObservedGeneration);
        Assert.Equal("assignment-2", result.Observations["reportedAssignmentId"]);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ProviderExecutionDiagnosticCodes.ResultAssignmentMismatch, diagnostic.Code);
        Assert.Equal(request.TargetResourceId, diagnostic.Target);

        Assert.NotNull(observation);
        Assert.Equal(request.AssignmentId, observation.AssignmentId);
        Assert.Equal(ProviderExecutionStatus.Failed, observation.Status);
        Assert.Null(observation.ObservedGeneration);
        Assert.Equal("assignment-2", observation.Observations["reportedAssignmentId"]);
        var observedDiagnostic = Assert.Single(observation.Diagnostics);
        Assert.Equal(ProviderExecutionDiagnosticCodes.ResultAssignmentMismatch, observedDiagnostic.Code);
        Assert.Equal(request.TargetResourceId, observedDiagnostic.Target);
    }

    [Fact]
    public async Task InProcessDispatcher_ReturnsFailedWhenRequestIsInvalid()
    {
        var handler = new RecordingExecutionHandler(
            ProviderExecutionInstructionTypes.ProcessStart,
            [ProviderExecutionCapabilities.Processes]);
        var observations = new InMemoryProviderExecutionObservationStore();
        var dispatcher = new InProcessProviderExecutionDispatcher([handler], observations);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = " ",
            TargetResourceId = "application:worker",
            DesiredGeneration = -1,
            IdempotencyKey = "",
            RequiredCapabilities = [ProviderExecutionCapabilities.Processes, ""]
        };

        var result = await dispatcher.ExecuteAsync(request);
        var observation = await observations.GetAsync(request.AssignmentId);

        Assert.Equal(request.AssignmentId, result.AssignmentId);
        Assert.Equal(ProviderExecutionStatus.Failed, result.Status);
        Assert.Null(result.ObservedGeneration);
        Assert.Null(handler.Request);
        Assert.All(
            result.Diagnostics,
            diagnostic =>
            {
                Assert.Equal(ProviderExecutionDiagnosticCodes.RequestInvalid, diagnostic.Code);
                Assert.Equal(request.TargetResourceId, diagnostic.Target);
            });
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message == "Provider execution instruction type is required.");
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message == "Provider execution desired generation cannot be negative.");
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message == "Provider execution idempotency key is required.");
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message == "Provider execution required capabilities cannot contain empty values.");

        Assert.NotNull(observation);
        Assert.Equal(ProviderExecutionStatus.Failed, observation.Status);
        Assert.Equal(result.Diagnostics, observation.Diagnostics);
    }

    [Fact]
    public async Task InProcessDispatcher_ReturnsFailedWhenSnapshotsDoNotMatchTarget()
    {
        var target = CreateGraphResource("network:private", "private");
        var other = CreateGraphResource("application:api", "api");
        var handler = new RecordingExecutionHandler(
            ProviderExecutionInstructionTypes.NetworkEndpointReconcile,
            [ProviderExecutionCapabilities.HostNetworking]);
        var dispatcher = new InProcessProviderExecutionDispatcher([handler]);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.NetworkEndpointReconcile,
            TargetResourceId = target.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "network:private:1",
            RequiredCapabilities = [ProviderExecutionCapabilities.HostNetworking],
            TargetResourceSnapshot = other,
            ResourceSnapshot = [other]
        };

        var result = await dispatcher.ExecuteAsync(request);

        Assert.Equal(request.AssignmentId, result.AssignmentId);
        Assert.Equal(ProviderExecutionStatus.Failed, result.Status);
        Assert.Null(handler.Request);
        Assert.All(
            result.Diagnostics,
            diagnostic =>
            {
                Assert.Equal(ProviderExecutionDiagnosticCodes.RequestInvalid, diagnostic.Code);
                Assert.Equal(request.TargetResourceId, diagnostic.Target);
            });
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message == "Provider execution target resource snapshot must match the target resource id.");
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message == "Provider execution resource snapshot must include the target resource when provided.");
    }

    [Fact]
    public async Task InProcessDispatcher_DoesNotRecordInvalidRequestWithoutAssignment()
    {
        var observations = new InMemoryProviderExecutionObservationStore();
        var dispatcher = new InProcessProviderExecutionDispatcher([], observations);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "",
            InstructionType = ProviderExecutionInstructionTypes.ProcessStart,
            TargetResourceId = "application:worker",
            DesiredGeneration = 1,
            IdempotencyKey = "application:worker:1"
        };

        var result = await dispatcher.ExecuteAsync(request);
        var recorded = await observations.ListAsync();

        Assert.Equal("", result.AssignmentId);
        Assert.Equal(ProviderExecutionStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ProviderExecutionDiagnosticCodes.RequestInvalid, diagnostic.Code);
        Assert.Equal(request.TargetResourceId, diagnostic.Target);
        Assert.Equal("Provider execution assignment id is required.", diagnostic.Message);
        Assert.Empty(recorded);
    }

    [Fact]
    public async Task InProcessDispatcher_ReturnsUnavailableWhenTargetIsAgent()
    {
        var handler = new RecordingExecutionHandler(
            ProviderExecutionInstructionTypes.ProcessStart,
            [ProviderExecutionCapabilities.Processes]);
        var dispatcher = new InProcessProviderExecutionDispatcher([handler]);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ProcessStart,
            TargetResourceId = "application:worker",
            DesiredGeneration = 3,
            IdempotencyKey = "application:worker:3",
            Target = new ProviderExecutionTarget
            {
                Kind = ProviderExecutionTargetKind.Agent,
                TargetId = "agent-a"
            },
            RequiredCapabilities = [ProviderExecutionCapabilities.Processes]
        };

        var result = await dispatcher.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Unavailable, result.Status);
        Assert.Null(handler.Request);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ProviderExecutionDiagnosticCodes.ExecutionTargetUnsupported, diagnostic.Code);
        Assert.Equal(request.TargetResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task InProcessDispatcher_ReturnsUnavailableWhenHandlerIsMissing()
    {
        var dispatcher = new InProcessProviderExecutionDispatcher([]);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ContainerStart,
            TargetResourceId = "container-app:orders",
            DesiredGeneration = 1,
            IdempotencyKey = "container-app:orders:1"
        };

        var result = await dispatcher.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Unavailable, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ProviderExecutionDiagnosticCodes.HandlerMissing, diagnostic.Code);
        Assert.Equal(request.TargetResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task InProcessDispatcher_RecordsUnavailableExecutionObservation()
    {
        var observations = new InMemoryProviderExecutionObservationStore();
        var dispatcher = new InProcessProviderExecutionDispatcher([], observations);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ContainerStart,
            TargetResourceId = "container-app:orders",
            DesiredGeneration = 1,
            IdempotencyKey = "container-app:orders:1"
        };

        await dispatcher.ExecuteAsync(request);

        var observation = await observations.GetAsync(request.AssignmentId);

        Assert.NotNull(observation);
        Assert.Equal(ProviderExecutionStatus.Unavailable, observation.Status);
        Assert.Null(observation.ObservedGeneration);
        var diagnostic = Assert.Single(observation.Diagnostics);
        Assert.Equal(ProviderExecutionDiagnosticCodes.HandlerMissing, diagnostic.Code);
        Assert.Equal(request.TargetResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task InProcessDispatcher_ReturnsUnavailableWhenCapabilitiesAreMissing()
    {
        var handler = new RecordingExecutionHandler(
            ProviderExecutionInstructionTypes.ContainerStart,
            [ProviderExecutionCapabilities.Containers]);
        var dispatcher = new InProcessProviderExecutionDispatcher([handler]);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ContainerStart,
            TargetResourceId = "container-app:orders",
            DesiredGeneration = 1,
            IdempotencyKey = "container-app:orders:1",
            RequiredCapabilities =
            [
                ProviderExecutionCapabilities.Containers,
                ProviderExecutionCapabilities.VolumeMounts
            ]
        };

        var result = await dispatcher.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Unavailable, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ProviderExecutionDiagnosticCodes.RequiredCapabilityMissing, diagnostic.Code);
        Assert.Equal(request.TargetResourceId, diagnostic.Target);
    }

    [Fact]
    public void ServiceRegistration_ValidatesSingletonOperationProvidersWithDispatcher()
    {
        var services = new ServiceCollection();
        services.AddProviderExecutionDispatcher();
        services.AddScoped<IProviderExecutionHandler>(_ =>
            new RecordingExecutionHandler(
                ProviderExecutionInstructionTypes.NetworkEndpointReconcile,
                [ProviderExecutionCapabilities.HostNetworking]));
        services.AddSingleton<
            IResourceOperationProvider,
            NetworkReconcileEndpointMappingsOperationProvider>();
        services.AddSingleton<
            IResourceOperationProjector,
            NetworkReconcileEndpointMappingsOperationProvider>();

        using var serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        Assert.NotNull(serviceProvider.GetRequiredService<IProviderExecutionDispatcher>());
        Assert.NotNull(serviceProvider.GetRequiredService<IProviderExecutionObservationStore>());
    }

    [Fact]
    public async Task Dispatcher_ResolvesScopedExecutionHandlersPerDispatch()
    {
        var services = new ServiceCollection();
        services.AddProviderExecutionDispatcher();
        services.AddScoped<ScopedExecutionHandlerState>();
        services.AddScoped<IProviderExecutionHandler, ScopedExecutionHandler>();

        using var serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

        var dispatcher = serviceProvider.GetRequiredService<IProviderExecutionDispatcher>();

        var first = await dispatcher.ExecuteAsync(CreateProviderExecutionRequest("assignment-1"));
        var second = await dispatcher.ExecuteAsync(CreateProviderExecutionRequest("assignment-2"));

        Assert.NotEqual(
            first.Observations["scopeId"],
            second.Observations["scopeId"]);
    }

    [Fact]
    public void Request_CreatesProjectionExecutionContextFromResourceSnapshot()
    {
        var network = CreateGraphResource("network:private", "private");
        var api = CreateGraphResource("application:api", "api");
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.NetworkEndpointReconcile,
            TargetResourceId = network.EffectiveResourceId,
            DesiredGeneration = 5,
            IdempotencyKey = "network:private:5",
            ResourceSnapshot = [api, network]
        };

        var context = request.TryCreateProjectionExecutionContext();

        Assert.NotNull(context);
        Assert.Same(network, context.Resource);
        Assert.Equal([api, network], context.Resources);
    }

    [Fact]
    public void Request_IgnoresMismatchedTargetSnapshotWhenCreatingProjectionExecutionContext()
    {
        var network = CreateGraphResource("network:private", "private");
        var api = CreateGraphResource("application:api", "api");
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.NetworkEndpointReconcile,
            TargetResourceId = network.EffectiveResourceId,
            DesiredGeneration = 5,
            IdempotencyKey = "network:private:5",
            TargetResourceSnapshot = api,
            ResourceSnapshot = [network, api]
        };

        var context = request.TryCreateProjectionExecutionContext();

        Assert.NotNull(context);
        Assert.Same(network, context.Resource);
        Assert.Equal([network, api], context.Resources);
    }

    [Fact]
    public void ResultProjection_AddsFallbackDiagnosticWhenDispatcherFailsWithoutDiagnostics()
    {
        var resource = CreateGraphResource("docker.host:local", "local");
        var result = new ProviderExecutionResult
        {
            AssignmentId = $"{resource.EffectiveResourceId}:{DockerHostResourceTypeProvider.Operations.Inspect}",
            Status = ProviderExecutionStatus.Failed,
            Diagnostics = []
        };

        var operationResult = result.ToResourceOperationExecutionResult(
            resource,
            DockerHostResourceTypeProvider.Operations.Inspect);

        var diagnostic = Assert.Single(operationResult.Diagnostics);
        Assert.Equal(ProviderExecutionDiagnosticCodes.ResultMissingDiagnostics, diagnostic.Code);
        Assert.Contains("Failed", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(resource.EffectiveResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task NetworkReconcileOperation_DispatchesEndpointReconcileInstruction()
    {
        var network = CreateGraphResource("network:private", "private", revision: 5);
        var api = CreateGraphResource("application:api", "api");
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new NetworkReconcileEndpointMappingsOperation(
            new ResourceProjectionExecutionContext(network, [network, api]),
            new ResourceOperationResolution(
                NetworkResourceTypeProvider.Operations.ReconcileEndpointMappings,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.NetworkEndpointReconcile, request.InstructionType);
        Assert.Equal(network.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(5, request.DesiredGeneration);
        Assert.Equal(
            $"{network.EffectiveResourceId}:{NetworkResourceTypeProvider.Operations.ReconcileEndpointMappings}:5",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.HostNetworking, request.RequiredCapabilities);
        Assert.Same(network, request.TargetResourceSnapshot);
        Assert.Equal([network, api], request.ResourceSnapshot);
    }

    [Fact]
    public async Task NetworkEndpointMappingHandler_ReturnsReconcilerDiagnostics()
    {
        var network = CreateGraphResource("network:private", "private");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "network.test",
            "Network reconciled.",
            network.EffectiveResourceId);
        var handler = new NetworkEndpointMappingExecutionHandler(
            new RecordingNetworkEndpointMappingReconciler([diagnostic]));
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.NetworkEndpointReconcile,
            TargetResourceId = network.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "network:private:1",
            TargetResourceSnapshot = network,
            ResourceSnapshot = [network]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
    }

    [Fact]
    public async Task NetworkEndpointMappingHandler_FailsWhenReconcilerIsMissing()
    {
        var network = CreateGraphResource("network:private", "private");
        var handler = new NetworkEndpointMappingExecutionHandler();
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.NetworkEndpointReconcile,
            TargetResourceId = network.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "network:private:1",
            TargetResourceSnapshot = network,
            ResourceSnapshot = [network]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("network.endpointMappingReconcilerMissing", diagnostic.Code);
        Assert.Equal(network.EffectiveResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task NetworkEndpointMappingHandler_ReturnsUnavailableWithoutResourceSnapshot()
    {
        var handler = new NetworkEndpointMappingExecutionHandler();
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.NetworkEndpointReconcile,
            TargetResourceId = "network:private",
            DesiredGeneration = 1,
            IdempotencyKey = "network:private:1"
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Unavailable, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ProviderExecutionDiagnosticCodes.ResourceSnapshotMissing, diagnostic.Code);
        Assert.Equal(request.TargetResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task VirtualNetworkReconcileOperation_DispatchesEndpointReconcileInstruction()
    {
        var network = CreateGraphResource("virtual-network:private", "private", revision: 8);
        var endpoint = CreateGraphResource("endpoint:api", "api");
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new VirtualNetworkReconcileEndpointMappingsOperation(
            new ResourceProjectionExecutionContext(network, [network, endpoint]),
            new ResourceOperationResolution(
                VirtualNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.VirtualNetworkEndpointReconcile, request.InstructionType);
        Assert.Equal(network.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(8, request.DesiredGeneration);
        Assert.Equal(
            $"{network.EffectiveResourceId}:{VirtualNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings}:8",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.VirtualNetworking, request.RequiredCapabilities);
        Assert.Same(network, request.TargetResourceSnapshot);
        Assert.Equal([network, endpoint], request.ResourceSnapshot);
    }

    [Fact]
    public async Task VirtualNetworkEndpointMappingHandler_ReturnsReconcilerDiagnostics()
    {
        var network = CreateGraphResource("virtual-network:private", "private");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "virtualNetwork.test",
            "Virtual network reconciled.",
            network.EffectiveResourceId);
        var handler = new VirtualNetworkEndpointMappingExecutionHandler(
            new RecordingVirtualNetworkEndpointMappingReconciler([diagnostic]));
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.VirtualNetworkEndpointReconcile,
            TargetResourceId = network.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "virtual-network:private:1",
            TargetResourceSnapshot = network,
            ResourceSnapshot = [network]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
    }

    [Fact]
    public async Task VirtualNetworkEndpointMappingHandler_FailsWhenReconcilerIsMissing()
    {
        var network = CreateGraphResource("virtual-network:private", "private");
        var handler = new VirtualNetworkEndpointMappingExecutionHandler();
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.VirtualNetworkEndpointReconcile,
            TargetResourceId = network.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "virtual-network:private:1",
            TargetResourceSnapshot = network,
            ResourceSnapshot = [network]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("network.virtual.endpointMappingReconcilerMissing", diagnostic.Code);
        Assert.Equal(network.EffectiveResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task LocalHostNetworkReconcileOperation_DispatchesEndpointReconcileInstruction()
    {
        var hostNetwork = CreateGraphResource("host-network:local", "local", revision: 2);
        var endpoint = CreateGraphResource("endpoint:api", "api");
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new LocalHostNetworkReconcileEndpointMappingsOperation(
            new ResourceProjectionExecutionContext(hostNetwork, [hostNetwork, endpoint]),
            new ResourceOperationResolution(
                LocalHostNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.LocalHostNetworkEndpointReconcile, request.InstructionType);
        Assert.Equal(hostNetwork.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(2, request.DesiredGeneration);
        Assert.Equal(
            $"{hostNetwork.EffectiveResourceId}:{LocalHostNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings}:2",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.HostNetworking, request.RequiredCapabilities);
        Assert.Same(hostNetwork, request.TargetResourceSnapshot);
        Assert.Equal([hostNetwork, endpoint], request.ResourceSnapshot);
    }

    [Fact]
    public async Task LocalHostNetworkEndpointMappingHandler_ReturnsReconcilerDiagnostics()
    {
        var hostNetwork = CreateGraphResource("host-network:local", "local");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "hostNetwork.local.test",
            "Local host network reconciled.",
            hostNetwork.EffectiveResourceId);
        var handler = new LocalHostNetworkEndpointMappingExecutionHandler(
            new RecordingLocalHostNetworkEndpointMappingReconciler([diagnostic]));
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.LocalHostNetworkEndpointReconcile,
            TargetResourceId = hostNetwork.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "host-network:local:1",
            TargetResourceSnapshot = hostNetwork,
            ResourceSnapshot = [hostNetwork]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
    }

    [Fact]
    public async Task LocalHostNetworkEndpointMappingHandler_FailsWhenReconcilerIsMissing()
    {
        var hostNetwork = CreateGraphResource("host-network:local", "local");
        var handler = new LocalHostNetworkEndpointMappingExecutionHandler();
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.LocalHostNetworkEndpointReconcile,
            TargetResourceId = hostNetwork.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "host-network:local:1",
            TargetResourceSnapshot = hostNetwork,
            ResourceSnapshot = [hostNetwork]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("hostNetworking.endpointMappingReconcilerMissing", diagnostic.Code);
        Assert.Equal(hostNetwork.EffectiveResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task MacOSHostNetworkReconcileOperation_DispatchesEndpointReconcileInstruction()
    {
        var hostNetwork = CreateGraphResource("host-network:macos", "macos", revision: 3);
        var endpoint = CreateGraphResource("endpoint:api", "api");
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new MacOSHostNetworkReconcileEndpointMappingsOperation(
            new ResourceProjectionExecutionContext(hostNetwork, [hostNetwork, endpoint]),
            new ResourceOperationResolution(
                MacOSHostNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.MacOSHostNetworkEndpointReconcile, request.InstructionType);
        Assert.Equal(hostNetwork.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(3, request.DesiredGeneration);
        Assert.Equal(
            $"{hostNetwork.EffectiveResourceId}:{MacOSHostNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings}:3",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.HostNetworking, request.RequiredCapabilities);
        Assert.Same(hostNetwork, request.TargetResourceSnapshot);
        Assert.Equal([hostNetwork, endpoint], request.ResourceSnapshot);
    }

    [Fact]
    public async Task MacOSHostNetworkEndpointMappingHandler_ReturnsReconcilerDiagnostics()
    {
        var hostNetwork = CreateGraphResource("host-network:macos", "macos");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "hostNetwork.macos.test",
            "macOS host network reconciled.",
            hostNetwork.EffectiveResourceId);
        var handler = new MacOSHostNetworkEndpointMappingExecutionHandler(
            new RecordingMacOSHostNetworkEndpointMappingReconciler([diagnostic]));
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.MacOSHostNetworkEndpointReconcile,
            TargetResourceId = hostNetwork.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "host-network:macos:1",
            TargetResourceSnapshot = hostNetwork,
            ResourceSnapshot = [hostNetwork]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
    }

    [Fact]
    public async Task MacOSHostNetworkEndpointMappingHandler_FailsWhenReconcilerIsMissing()
    {
        var hostNetwork = CreateGraphResource("host-network:macos", "macos");
        var handler = new MacOSHostNetworkEndpointMappingExecutionHandler();
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.MacOSHostNetworkEndpointReconcile,
            TargetResourceId = hostNetwork.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "host-network:macos:1",
            TargetResourceSnapshot = hostNetwork,
            ResourceSnapshot = [hostNetwork]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("hostNetworking.macos.endpointMappingReconcilerMissing", diagnostic.Code);
        Assert.Equal(hostNetwork.EffectiveResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task DnsZoneReconcileOperation_DispatchesNameMappingInstruction()
    {
        var zone = CreateGraphResource("dns-zone:private", "private", revision: 6);
        var mapping = CreateGraphResource("name-mapping:api", "api");
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new DnsZoneReconcileNameMappingsOperation(
            new ResourceProjectionExecutionContext(zone, [zone, mapping]),
            new ResourceOperationResolution(
                DnsZoneResourceTypeProvider.Operations.ReconcileNameMappings,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.DnsNameMappingReconcile, request.InstructionType);
        Assert.Equal(zone.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(6, request.DesiredGeneration);
        Assert.Equal(
            $"{zone.EffectiveResourceId}:{DnsZoneResourceTypeProvider.Operations.ReconcileNameMappings}:6",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.DnsNameMappings, request.RequiredCapabilities);
        Assert.Same(zone, request.TargetResourceSnapshot);
        Assert.Equal([zone, mapping], request.ResourceSnapshot);
    }

    [Fact]
    public async Task DnsZoneNameMappingHandler_ReturnsReconcilerDiagnostics()
    {
        var zone = CreateGraphResource("dns-zone:private", "private");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "dns.test",
            "Name mappings reconciled.",
            zone.EffectiveResourceId);
        var handler = new DnsZoneNameMappingExecutionHandler(
            new RecordingDnsZoneNameMappingReconciler([diagnostic]));
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.DnsNameMappingReconcile,
            TargetResourceId = zone.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "dns-zone:private:1",
            TargetResourceSnapshot = zone,
            ResourceSnapshot = [zone]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
    }

    [Fact]
    public async Task DnsZoneNameMappingHandler_FailsWhenReconcilerIsMissing()
    {
        var zone = CreateGraphResource(
            "dns-zone:private",
            "private",
            attributes: new Dictionary<ResourceAttributeId, string>
            {
                [DnsZoneResourceTypeProvider.Attributes.Provider] = "hosts-file"
            });
        var handler = new DnsZoneNameMappingExecutionHandler();
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.DnsNameMappingReconcile,
            TargetResourceId = zone.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "dns-zone:private:1",
            TargetResourceSnapshot = zone,
            ResourceSnapshot = [zone]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("dns.zone.nameMappingReconcilerMissing", diagnostic.Code);
        Assert.Equal(zone.EffectiveResourceId, diagnostic.Target);
        Assert.Contains("provider 'hosts-file'", diagnostic.Message);
        Assert.Contains(zone.EffectiveResourceId, diagnostic.Message);
    }

    [Fact]
    public async Task LocalVolumeProvisionOperation_DispatchesFileSystemProvisionInstruction()
    {
        var volume = CreateGraphResource(
            "volume:data",
            "data",
            revision: 2,
            attributes: new Dictionary<ResourceAttributeId, string>
            {
                [LocalVolumeResourceTypeProvider.Attributes.StorageMedium] =
                    CloudShell.Abstractions.ResourceManager.StorageMedia.FileSystem
            });
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new LocalVolumeProvisionOperation(
            new ResourceProjectionExecutionContext(volume, [volume]),
            new ResourceOperationResolution(
                LocalVolumeResourceTypeProvider.Operations.Provision,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.FileSystemProvision, request.InstructionType);
        Assert.Equal(volume.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(2, request.DesiredGeneration);
        Assert.Equal(
            $"{volume.EffectiveResourceId}:{LocalVolumeResourceTypeProvider.Operations.Provision}:2",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.FileSystem, request.RequiredCapabilities);
        Assert.Same(volume, request.TargetResourceSnapshot);
        Assert.Equal([volume], request.ResourceSnapshot);
    }

    [Fact]
    public async Task LocalVolumeProvisionHandler_ReturnsProvisionerDiagnostics()
    {
        var volume = CreateGraphResource("volume:data", "data");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "volume.test",
            "Volume provisioned.",
            volume.EffectiveResourceId);
        var handler = new LocalVolumeProvisionExecutionHandler(
            new RecordingLocalVolumeProvisioner([diagnostic]));
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.FileSystemProvision,
            TargetResourceId = volume.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "volume:data:1",
            TargetResourceSnapshot = volume,
            ResourceSnapshot = [volume]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
    }

    [Fact]
    public async Task LocalVolumeProvisionHandler_FailsWhenProvisionerIsMissing()
    {
        var volume = CreateGraphResource("volume:data", "data");
        var handler = new LocalVolumeProvisionExecutionHandler();
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.FileSystemProvision,
            TargetResourceId = volume.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "volume:data:1",
            TargetResourceSnapshot = volume,
            ResourceSnapshot = [volume]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("storage.volume.provisionerMissing", diagnostic.Code);
        Assert.Equal(volume.EffectiveResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task SqlServerReconcileAccessOperation_DispatchesAccessReconcileInstruction()
    {
        var sqlServer = CreateGraphResource("sql-server:app", "app", revision: 4);
        var database = CreateGraphResource("sql-database:orders", "orders");
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new SqlServerReconcileAccessOperation(
            new ResourceProjectionExecutionContext(sqlServer, [sqlServer, database]),
            new ResourceOperationResolution(
                SqlServerResourceTypeProvider.Operations.ReconcileAccess,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.SqlServerAccessReconcile, request.InstructionType);
        Assert.Equal(sqlServer.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(4, request.DesiredGeneration);
        Assert.Equal(
            $"{sqlServer.EffectiveResourceId}:{SqlServerResourceTypeProvider.Operations.ReconcileAccess}:4",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.SqlServerAccess, request.RequiredCapabilities);
        Assert.Same(sqlServer, request.TargetResourceSnapshot);
        Assert.Equal([sqlServer, database], request.ResourceSnapshot);
    }

    [Fact]
    public async Task SqlServerAccessReconcileHandler_ReturnsReconcilerDiagnostics()
    {
        var sqlServer = CreateGraphResource("sql-server:app", "app");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "sqlServer.test",
            "SQL Server access reconciled.",
            sqlServer.EffectiveResourceId);
        var handler = new SqlServerAccessReconcileExecutionHandler(
            new RecordingSqlServerAccessReconciler([diagnostic]));
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.SqlServerAccessReconcile,
            TargetResourceId = sqlServer.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "sql-server:app:1",
            TargetResourceSnapshot = sqlServer,
            ResourceSnapshot = [sqlServer]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
    }

    [Fact]
    public async Task SqlServerAccessReconcileHandler_FailsWhenReconcilerIsMissing()
    {
        var sqlServer = CreateGraphResource("sql-server:app", "app");
        var handler = new SqlServerAccessReconcileExecutionHandler();
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.SqlServerAccessReconcile,
            TargetResourceId = sqlServer.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "sql-server:app:1",
            TargetResourceSnapshot = sqlServer,
            ResourceSnapshot = [sqlServer]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("application.sqlServer.accessReconcilerMissing", diagnostic.Code);
        Assert.Equal(sqlServer.EffectiveResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task LoadBalancerApplyConfigurationOperation_DispatchesApplyInstruction()
    {
        var loadBalancer = CreateGraphResource("load-balancer:public", "public", revision: 12);
        var route = CreateGraphResource("load-balancer-route:api", "api");
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new LoadBalancerApplyConfigurationOperation(
            new ResourceProjectionExecutionContext(loadBalancer, [loadBalancer, route]),
            new ResourceOperationResolution(
                LoadBalancerResourceTypeProvider.Operations.ApplyConfiguration,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.LoadBalancerConfigurationApply, request.InstructionType);
        Assert.Equal(loadBalancer.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(12, request.DesiredGeneration);
        Assert.Equal(
            $"{loadBalancer.EffectiveResourceId}:{LoadBalancerResourceTypeProvider.Operations.ApplyConfiguration}:12",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.LoadBalancing, request.RequiredCapabilities);
        Assert.Same(loadBalancer, request.TargetResourceSnapshot);
        Assert.Equal([loadBalancer, route], request.ResourceSnapshot);
    }

    [Fact]
    public async Task LoadBalancerConfigurationApplyHandler_ReturnsApplierDiagnostics()
    {
        var loadBalancer = CreateGraphResource("load-balancer:public", "public");
        var route = CreateGraphResource("load-balancer-route:api", "api");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "loadBalancer.configuration.test",
            "Load balancer configuration applied.",
            loadBalancer.EffectiveResourceId);
        var applier = new RecordingLoadBalancerConfigurationApplier([diagnostic]);
        var handler = new LoadBalancerConfigurationApplyExecutionHandler(applier);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.LoadBalancerConfigurationApply,
            TargetResourceId = loadBalancer.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "load-balancer:public:apply:1",
            TargetResourceSnapshot = loadBalancer,
            ResourceSnapshot = [loadBalancer, route]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        Assert.Same(loadBalancer, applier.Resource);
        Assert.NotNull(applier.Context);
        Assert.Same(loadBalancer, applier.Context.Resource);
        Assert.Equal([loadBalancer, route], applier.Context.Resources);
    }

    [Fact]
    public async Task LoadBalancerConfigurationApplyHandler_FailsWhenConfigurationApplierIsMissing()
    {
        var loadBalancer = CreateGraphResource(
            "load-balancer:public",
            "public",
            attributes: new Dictionary<ResourceAttributeId, string>
            {
                [LoadBalancerResourceTypeProvider.Attributes.Provider] = "traefik"
            });
        var handler = new LoadBalancerConfigurationApplyExecutionHandler();
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.LoadBalancerConfigurationApply,
            TargetResourceId = loadBalancer.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "load-balancer:public:apply:1",
            TargetResourceSnapshot = loadBalancer,
            ResourceSnapshot = [loadBalancer]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("network.loadBalancer.configurationApplierMissing", diagnostic.Code);
        Assert.Equal(loadBalancer.EffectiveResourceId, diagnostic.Target);
        Assert.Contains("provider 'traefik'", diagnostic.Message);
        Assert.Contains(loadBalancer.EffectiveResourceId, diagnostic.Message);
    }

    [Fact]
    public async Task LoadBalancerConfigurationApplyHandler_ReturnsRouteResolutionDiagnostics()
    {
        var loadBalancer = CreateGraphResourceWithAttributeValues(
            "load-balancer:public",
            "public",
            new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [LoadBalancerResourceTypeProvider.Attributes.Provider] =
                    ResourceAttributeValue.String("traefik"),
                [LoadBalancerResourceTypeProvider.Attributes.Entrypoints] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new LoadBalancerEntrypointValue(
                            "web",
                            "Http",
                            8080)
                    }),
                [LoadBalancerResourceTypeProvider.Attributes.Routes] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new LoadBalancerRouteValue(
                            "api",
                            "API",
                            "Http",
                            "web",
                            new LoadBalancerRouteMatchValue("api.local", "/"),
                            new LoadBalancerRouteTargetValue(
                                ResourceReference.ReferenceResourceId("application:api"),
                                "http"))
                    })
            });
        var handler = new LoadBalancerConfigurationApplyExecutionHandler(
            new ResourceModelGraphTraefikLoadBalancerConfigurationApplier(
                new TraefikLoadBalancerProvider(new TraefikProviderOptions
                {
                    DynamicConfigurationDirectory = CreateTempDirectory()
                })));
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.LoadBalancerConfigurationApply,
            TargetResourceId = loadBalancer.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "load-balancer:public:apply:1",
            TargetResourceSnapshot = loadBalancer,
            ResourceSnapshot = [loadBalancer]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("network.loadBalancer.routeResolutionFailed", diagnostic.Code);
        Assert.Equal(loadBalancer.EffectiveResourceId, diagnostic.Target);
        Assert.Contains("application:api", diagnostic.Message);
    }

    [Fact]
    public async Task CloudShellVolumeProvisionOperation_DispatchesProvisionInstruction()
    {
        var volume = CreateGraphResource(
            "volume:data",
            "data",
            revision: 13,
            attributes: new Dictionary<ResourceAttributeId, string>
            {
                [CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium] = "local"
            });
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new CloudShellVolumeProvisionOperation(
            new ResourceProjectionExecutionContext(volume, [volume]),
            new ResourceOperationResolution(
                CloudShellVolumeResourceTypeProvider.Operations.Provision,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.StorageVolumeProvision, request.InstructionType);
        Assert.Equal(volume.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(13, request.DesiredGeneration);
        Assert.Equal(
            $"{volume.EffectiveResourceId}:{CloudShellVolumeResourceTypeProvider.Operations.Provision}:13",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.Storage, request.RequiredCapabilities);
        Assert.Same(volume, request.TargetResourceSnapshot);
        Assert.Equal([volume], request.ResourceSnapshot);
    }

    [Fact]
    public async Task CloudShellVolumeProvisionHandler_ReturnsProvisionerDiagnostics()
    {
        var volume = CreateGraphResource("volume:data", "data");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "volume.provision.test",
            "Volume provisioned.",
            volume.EffectiveResourceId);
        var provisioner = new RecordingCloudShellVolumeProvisioner([diagnostic]);
        var handler = new CloudShellVolumeProvisionExecutionHandler(provisioner);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.StorageVolumeProvision,
            TargetResourceId = volume.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "volume:data:provision:1",
            TargetResourceSnapshot = volume,
            ResourceSnapshot = [volume]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        Assert.Same(volume, Assert.Single(provisioner.Invocations));
    }

    [Fact]
    public async Task CloudShellVolumeProvisionHandler_FailsWhenProvisionerIsMissing()
    {
        var volume = CreateGraphResource("volume:data", "data");
        var handler = new CloudShellVolumeProvisionExecutionHandler();
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.StorageVolumeProvision,
            TargetResourceId = volume.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "volume:data:provision:1",
            TargetResourceSnapshot = volume,
            ResourceSnapshot = [volume]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("cloudshell.volume.provisionerMissing", diagnostic.Code);
        Assert.Equal(volume.EffectiveResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task ExecutableStartOperation_DispatchesStartInstruction()
    {
        var executable = CreateGraphResource("executable:worker", "worker", revision: 14);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new ExecutableStartOperation(
            new ResourceProjectionExecutionContext(executable, [executable]),
            new ResourceOperationResolution(
                ExecutableApplicationResourceTypeProvider.Operations.Start,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.ExecutableApplicationStart, request.InstructionType);
        Assert.Equal(executable.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(14, request.DesiredGeneration);
        Assert.Equal(
            $"{executable.EffectiveResourceId}:{ExecutableApplicationResourceTypeProvider.Operations.Start}:14",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.Processes, request.RequiredCapabilities);
        Assert.Same(executable, request.TargetResourceSnapshot);
        Assert.Equal([executable], request.ResourceSnapshot);
    }

    [Fact]
    public async Task ExecutableApplicationStartHandler_ReturnsRuntimeDiagnostics()
    {
        var executable = CreateGraphResource("executable:worker", "worker");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "executable.start.test",
            "Executable application started.",
            executable.EffectiveResourceId);
        var runtimeController = new RecordingExecutableApplicationRuntimeController([diagnostic]);
        var handler = new ExecutableApplicationStartExecutionHandler(runtimeController);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ExecutableApplicationStart,
            TargetResourceId = executable.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "executable:worker:start:1",
            TargetResourceSnapshot = executable,
            ResourceSnapshot = [executable]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        Assert.Same(executable, Assert.Single(runtimeController.StartInvocations));
    }

    [Fact]
    public async Task AspNetCoreProjectLifecycleOperation_DispatchesLifecycleInstruction()
    {
        var project = CreateGraphResource("application:api", "api", revision: 17);
        var dispatcher = new RecordingExecutionDispatcher();
        var runtimeController = new RecordingAspNetCoreProjectRuntimeController(
            status: AspNetCoreProjectRuntimeStatus.Stopped);
        var operation = new AspNetCoreProjectLifecycleOperation(
            new ResourceProjectionExecutionContext(project, [project]),
            new ResourceOperationResolution(
                AspNetCoreProjectResourceTypeProvider.Operations.Start,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            runtimeController,
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.AspNetCoreProjectStart, request.InstructionType);
        Assert.Equal(project.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(17, request.DesiredGeneration);
        Assert.Equal(
            $"{project.EffectiveResourceId}:{AspNetCoreProjectResourceTypeProvider.Operations.Start}:17",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.Processes, request.RequiredCapabilities);
        Assert.Same(project, request.TargetResourceSnapshot);
        Assert.Equal([project], request.ResourceSnapshot);
    }

    [Fact]
    public async Task AspNetCoreProjectLifecycleHandler_ReturnsRuntimeDiagnostics()
    {
        var project = CreateGraphResource("application:api", "api");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "aspNetCoreProject.start.test",
            "ASP.NET Core project started.",
            project.EffectiveResourceId);
        var runtimeController = new RecordingAspNetCoreProjectRuntimeController([diagnostic]);
        var handler = new AspNetCoreProjectStartExecutionHandler(runtimeController);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.AspNetCoreProjectStart,
            TargetResourceId = project.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "application:api:start:1",
            TargetResourceSnapshot = project,
            ResourceSnapshot = [project]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        var invocation = Assert.Single(runtimeController.LifecycleInvocations);
        Assert.Same(project, invocation.Resource);
        Assert.Equal(AspNetCoreProjectResourceTypeProvider.Operations.Start, invocation.OperationId);
    }

    [Fact]
    public async Task JavaScriptAppLifecycleOperation_DispatchesLifecycleInstruction()
    {
        var app = CreateGraphResource("application.javascript-app:frontend", "frontend", revision: 19);
        var dispatcher = new RecordingExecutionDispatcher();
        var runtimeController = new RecordingJavaScriptAppRuntimeController(
            status: JavaScriptAppRuntimeStatus.Stopped);
        var operation = new JavaScriptAppLifecycleOperation(
            new ResourceProjectionExecutionContext(app, [app]),
            new ResourceOperationResolution(
                JavaScriptAppResourceTypeProvider.Operations.Start,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            runtimeController,
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.JavaScriptAppStart, request.InstructionType);
        Assert.Equal(app.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(19, request.DesiredGeneration);
        Assert.Equal(
            $"{app.EffectiveResourceId}:{JavaScriptAppResourceTypeProvider.Operations.Start}:19",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.Processes, request.RequiredCapabilities);
        Assert.Same(app, request.TargetResourceSnapshot);
        Assert.Equal([app], request.ResourceSnapshot);
    }

    [Fact]
    public async Task JavaScriptAppLifecycleHandler_ReturnsRuntimeDiagnostics()
    {
        var app = CreateGraphResource("application.javascript-app:frontend", "frontend");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "javascriptApp.start.test",
            "JavaScript app started.",
            app.EffectiveResourceId);
        var runtimeController = new RecordingJavaScriptAppRuntimeController([diagnostic]);
        var handler = new JavaScriptAppStartExecutionHandler(runtimeController);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.JavaScriptAppStart,
            TargetResourceId = app.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "application.javascript-app:frontend:start:1",
            TargetResourceSnapshot = app,
            ResourceSnapshot = [app]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        var invocation = Assert.Single(runtimeController.LifecycleInvocations);
        Assert.Same(app, invocation.Resource);
        Assert.Equal(JavaScriptAppResourceTypeProvider.Operations.Start, invocation.OperationId);
    }

    [Fact]
    public async Task JavaAppLifecycleOperation_DispatchesLifecycleInstruction()
    {
        var app = CreateGraphResource("application.java-app:api", "api", revision: 23);
        var dispatcher = new RecordingExecutionDispatcher();
        var runtimeController = new RecordingJavaAppRuntimeController(
            status: JavaAppRuntimeStatus.Stopped);
        var operation = new JavaAppLifecycleOperation(
            new ResourceProjectionExecutionContext(app, [app]),
            new ResourceOperationResolution(
                JavaAppResourceTypeProvider.Operations.Start,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            runtimeController,
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.JavaAppStart, request.InstructionType);
        Assert.Equal(app.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(23, request.DesiredGeneration);
        Assert.Equal(
            $"{app.EffectiveResourceId}:{JavaAppResourceTypeProvider.Operations.Start}:23",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.Processes, request.RequiredCapabilities);
        Assert.Same(app, request.TargetResourceSnapshot);
        Assert.Equal([app], request.ResourceSnapshot);
    }

    [Fact]
    public async Task JavaAppLifecycleHandler_ReturnsRuntimeDiagnostics()
    {
        var app = CreateGraphResource("application.java-app:api", "api");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "javaApp.start.test",
            "Java app started.",
            app.EffectiveResourceId);
        var runtimeController = new RecordingJavaAppRuntimeController([diagnostic]);
        var handler = new JavaAppStartExecutionHandler(runtimeController);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.JavaAppStart,
            TargetResourceId = app.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "application.java-app:api:start:1",
            TargetResourceSnapshot = app,
            ResourceSnapshot = [app]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        var invocation = Assert.Single(runtimeController.LifecycleInvocations);
        Assert.Same(app, invocation.Resource);
        Assert.Equal(JavaAppResourceTypeProvider.Operations.Start, invocation.OperationId);
    }

    [Fact]
    public async Task GoAppLifecycleOperation_DispatchesLifecycleInstruction()
    {
        var app = CreateGraphResource("application.go-app:api", "api", revision: 29);
        var dispatcher = new RecordingExecutionDispatcher();
        var runtimeController = new RecordingGoAppRuntimeController(
            status: GoAppRuntimeStatus.Stopped);
        var operation = new GoAppLifecycleOperation(
            new ResourceProjectionExecutionContext(app, [app]),
            new ResourceOperationResolution(
                GoAppResourceTypeProvider.Operations.Start,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            runtimeController,
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.GoAppStart, request.InstructionType);
        Assert.Equal(app.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(29, request.DesiredGeneration);
        Assert.Equal(
            $"{app.EffectiveResourceId}:{GoAppResourceTypeProvider.Operations.Start}:29",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.Processes, request.RequiredCapabilities);
        Assert.Same(app, request.TargetResourceSnapshot);
        Assert.Equal([app], request.ResourceSnapshot);
    }

    [Fact]
    public async Task GoAppLifecycleHandler_ReturnsRuntimeDiagnostics()
    {
        var app = CreateGraphResource("application.go-app:api", "api");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "goApp.start.test",
            "Go app started.",
            app.EffectiveResourceId);
        var runtimeController = new RecordingGoAppRuntimeController([diagnostic]);
        var handler = new GoAppStartExecutionHandler(runtimeController);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.GoAppStart,
            TargetResourceId = app.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "application.go-app:api:start:1",
            TargetResourceSnapshot = app,
            ResourceSnapshot = [app]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        var invocation = Assert.Single(runtimeController.LifecycleInvocations);
        Assert.Same(app, invocation.Resource);
        Assert.Equal(GoAppResourceTypeProvider.Operations.Start, invocation.OperationId);
    }

    [Fact]
    public async Task PythonAppLifecycleOperation_DispatchesLifecycleInstruction()
    {
        var app = CreateGraphResource("application.python-app:api", "api", revision: 31);
        var dispatcher = new RecordingExecutionDispatcher();
        var runtimeController = new RecordingPythonAppRuntimeController(
            status: PythonAppRuntimeStatus.Stopped);
        var operation = new PythonAppLifecycleOperation(
            new ResourceProjectionExecutionContext(app, [app]),
            new ResourceOperationResolution(
                PythonAppResourceTypeProvider.Operations.Start,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            runtimeController,
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.PythonAppStart, request.InstructionType);
        Assert.Equal(app.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(31, request.DesiredGeneration);
        Assert.Equal(
            $"{app.EffectiveResourceId}:{PythonAppResourceTypeProvider.Operations.Start}:31",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.Processes, request.RequiredCapabilities);
        Assert.Same(app, request.TargetResourceSnapshot);
        Assert.Equal([app], request.ResourceSnapshot);
    }

    [Fact]
    public async Task PythonAppLifecycleHandler_ReturnsRuntimeDiagnostics()
    {
        var app = CreateGraphResource("application.python-app:api", "api");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "pythonApp.start.test",
            "Python app started.",
            app.EffectiveResourceId);
        var runtimeController = new RecordingPythonAppRuntimeController([diagnostic]);
        var handler = new PythonAppStartExecutionHandler(runtimeController);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.PythonAppStart,
            TargetResourceId = app.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "application.python-app:api:start:1",
            TargetResourceSnapshot = app,
            ResourceSnapshot = [app]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        var invocation = Assert.Single(runtimeController.LifecycleInvocations);
        Assert.Same(app, invocation.Resource);
        Assert.Equal(PythonAppResourceTypeProvider.Operations.Start, invocation.OperationId);
    }

    [Fact]
    public async Task DeviceRegistryLifecycleOperation_DispatchesLifecycleInstruction()
    {
        var registry = CreateGraphResource("iot.device-registry:devices", "devices", revision: 33);
        var dispatcher = new RecordingExecutionDispatcher();
        var runtimeController = new RecordingDeviceRegistryRuntimeController(
            status: ResourceWebAppRuntimeStatus.Stopped);
        var operation = new DeviceRegistryLifecycleOperation(
            new ResourceProjectionExecutionContext(registry, [registry]),
            new ResourceOperationResolution(
                DeviceRegistryResourceTypeProvider.Operations.Start,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            runtimeController,
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.DeviceRegistryStart, request.InstructionType);
        Assert.Equal(registry.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(33, request.DesiredGeneration);
        Assert.Equal(
            $"{registry.EffectiveResourceId}:{DeviceRegistryResourceTypeProvider.Operations.Start}:33",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.Processes, request.RequiredCapabilities);
        Assert.Same(registry, request.TargetResourceSnapshot);
        Assert.Equal([registry], request.ResourceSnapshot);
    }

    [Fact]
    public async Task DeviceRegistryLifecycleHandler_ReturnsRuntimeDiagnostics()
    {
        var registry = CreateGraphResource("iot.device-registry:devices", "devices");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "deviceRegistry.start.test",
            "Device Registry started.",
            registry.EffectiveResourceId);
        var runtimeController = new RecordingDeviceRegistryRuntimeController([diagnostic]);
        var handler = new DeviceRegistryStartExecutionHandler(runtimeController);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.DeviceRegistryStart,
            TargetResourceId = registry.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "iot.device-registry:devices:start:1",
            TargetResourceSnapshot = registry,
            ResourceSnapshot = [registry]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        var invocation = Assert.Single(runtimeController.LifecycleInvocations);
        Assert.Same(registry, invocation.Resource);
        Assert.Equal(DeviceRegistryResourceTypeProvider.Operations.Start, invocation.OperationId);
    }

    [Fact]
    public async Task IdentityProvisioningSetupOperation_DispatchesSetupInstruction()
    {
        var identityProvisioning = CreateGraphResource(
            "cloudshell.identity-provisioning:keycloak",
            "keycloak",
            revision: 37);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new IdentityProvisioningSetupOperation(
            new ResourceProjectionExecutionContext(identityProvisioning, [identityProvisioning]),
            new ResourceOperationResolution(
                IdentityProvisioningResourceTypeProvider.Operations.Setup,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.IdentityProvisioningSetup, request.InstructionType);
        Assert.Equal(identityProvisioning.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(37, request.DesiredGeneration);
        Assert.Equal(
            $"{identityProvisioning.EffectiveResourceId}:{IdentityProvisioningResourceTypeProvider.Operations.Setup}:37",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.IdentityProvisioning, request.RequiredCapabilities);
        Assert.Same(identityProvisioning, request.TargetResourceSnapshot);
        Assert.Equal([identityProvisioning], request.ResourceSnapshot);
    }

    [Fact]
    public async Task IdentityProvisioningSetupHandler_ReturnsSetupDiagnostics()
    {
        var identityProvisioning = CreateGraphResource(
            "cloudshell.identity-provisioning:keycloak",
            "keycloak");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "identityProvisioning.setup.test",
            "Identity provider setup completed.",
            identityProvisioning.EffectiveResourceId);
        var setupHandler = new RecordingIdentityProvisioningSetupHandler([diagnostic]);
        var handler = new IdentityProvisioningSetupExecutionHandler(setupHandler);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.IdentityProvisioningSetup,
            TargetResourceId = identityProvisioning.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "cloudshell.identity-provisioning:keycloak:setup:1",
            TargetResourceSnapshot = identityProvisioning,
            ResourceSnapshot = [identityProvisioning]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        Assert.Same(identityProvisioning, Assert.Single(setupHandler.SetupInvocations));
    }

    [Fact]
    public async Task IdentityProvisioningSetupHandler_FailsWhenSetupHandlerIsMissing()
    {
        var identityProvisioning = CreateGraphResource(
            "cloudshell.identity-provisioning:keycloak",
            "keycloak");
        var handler = new IdentityProvisioningSetupExecutionHandler();
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.IdentityProvisioningSetup,
            TargetResourceId = identityProvisioning.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "cloudshell.identity-provisioning:keycloak:setup:1",
            TargetResourceSnapshot = identityProvisioning,
            ResourceSnapshot = [identityProvisioning]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("identity.provisioning.setupHandlerMissing", diagnostic.Code);
        Assert.Equal(identityProvisioning.EffectiveResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task SqlDatabaseEnsureCreatedOperation_DispatchesEnsureCreatedInstruction()
    {
        var sqlServer = CreateGraphResource("application.sql-server:app", "app-sql");
        var database = CreateGraphResource(
            "application.sql-database:app",
            "app-db",
            revision: 41,
            dependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    sqlServer.EffectiveResourceId,
                    SqlServerResourceTypeProvider.ResourceTypeId,
                    SqlServerResourceTypeProvider.ProviderId)
            ]);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new SqlDatabaseEnsureCreatedOperation(
            new ResourceProjectionExecutionContext(database, [database, sqlServer]),
            new ResourceOperationResolution(
                SqlDatabaseResourceTypeProvider.Operations.EnsureCreated,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.SqlDatabaseEnsureCreated, request.InstructionType);
        Assert.Equal(database.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(41, request.DesiredGeneration);
        Assert.Equal(
            $"{database.EffectiveResourceId}:{SqlDatabaseResourceTypeProvider.Operations.EnsureCreated}:41",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.SqlDatabaseCreation, request.RequiredCapabilities);
        Assert.Same(database, request.TargetResourceSnapshot);
        Assert.Equal([database, sqlServer], request.ResourceSnapshot);
    }

    [Fact]
    public async Task SqlDatabaseEnsureCreatedHandler_ReturnsCreationDiagnostics()
    {
        var sqlServer = CreateGraphResource("application.sql-server:app", "app-sql");
        var database = CreateGraphResource(
            "application.sql-database:app",
            "app-db",
            dependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    sqlServer.EffectiveResourceId,
                    SqlServerResourceTypeProvider.ResourceTypeId,
                    SqlServerResourceTypeProvider.ProviderId)
            ]);
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "sqlDatabase.ensureCreated.test",
            "SQL database exists.",
            database.EffectiveResourceId);
        var creationHandler = new RecordingSqlDatabaseCreationHandler([diagnostic]);
        var handler = new SqlDatabaseEnsureCreatedExecutionHandler(creationHandler);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.SqlDatabaseEnsureCreated,
            TargetResourceId = database.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "application.sql-database:app:ensure-created:1",
            TargetResourceSnapshot = database,
            ResourceSnapshot = [database, sqlServer]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        var context = Assert.Single(creationHandler.CreationInvocations);
        Assert.Same(database, context.Database);
        Assert.Same(sqlServer, context.Server);
    }

    [Fact]
    public async Task ServiceReconcileOperation_DispatchesReconcileInstruction()
    {
        var service = CreateGraphResource("service:orders", "orders", revision: 43);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new ServiceReconcileOperation(
            new ResourceProjectionExecutionContext(service, [service]),
            new ResourceOperationResolution(
                ServiceResourceTypeProvider.Operations.Reconcile,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.ServiceReconcile, request.InstructionType);
        Assert.Equal(service.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(43, request.DesiredGeneration);
        Assert.Equal(
            $"{service.EffectiveResourceId}:{ServiceResourceTypeProvider.Operations.Reconcile}:43",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.ServiceReconciliation, request.RequiredCapabilities);
        Assert.Same(service, request.TargetResourceSnapshot);
        Assert.Equal([service], request.ResourceSnapshot);
    }

    [Fact]
    public async Task ServiceReconcileHandler_ReturnsReconcilerDiagnostics()
    {
        var service = CreateGraphResource("service:orders", "orders");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "service.reconcile.test",
            "Service reconciled.",
            service.EffectiveResourceId);
        var reconciler = new RecordingServiceReconciler([diagnostic]);
        var handler = new ServiceReconcileExecutionHandler(reconciler);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ServiceReconcile,
            TargetResourceId = service.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "service:orders:reconcile:1",
            TargetResourceSnapshot = service,
            ResourceSnapshot = [service]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        Assert.Same(service, Assert.Single(reconciler.ReconcileInvocations));
    }

    [Fact]
    public async Task ServiceReconcileHandler_FailsWhenReconcilerIsMissing()
    {
        var service = CreateGraphResource("service:orders", "orders");
        var handler = new ServiceReconcileExecutionHandler();
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ServiceReconcile,
            TargetResourceId = service.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "service:orders:reconcile:1",
            TargetResourceSnapshot = service,
            ResourceSnapshot = [service]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("service.reconcilerMissing", diagnostic.Code);
        Assert.Equal(service.EffectiveResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task DockerHostInspectOperation_DispatchesInspectInstruction()
    {
        var dockerHost = CreateGraphResource("docker.host:local", "local", revision: 47);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new DockerHostInspectOperation(
            new ResourceProjectionExecutionContext(dockerHost, [dockerHost]),
            new ResourceOperationResolution(
                DockerHostResourceTypeProvider.Operations.Inspect,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.DockerHostInspect, request.InstructionType);
        Assert.Equal(dockerHost.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(47, request.DesiredGeneration);
        Assert.Equal(
            $"{dockerHost.EffectiveResourceId}:{DockerHostResourceTypeProvider.Operations.Inspect}:47",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.RuntimeObservation, request.RequiredCapabilities);
        Assert.Same(dockerHost, request.TargetResourceSnapshot);
        Assert.Equal([dockerHost], request.ResourceSnapshot);
    }

    [Fact]
    public async Task DockerHostInspectHandler_ReturnsInspectorDiagnostics()
    {
        var dockerHost = CreateGraphResource("docker.host:local", "local");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "dockerHost.inspect.test",
            "Docker host inspected.",
            dockerHost.EffectiveResourceId);
        var inspector = new RecordingDockerHostInspector([diagnostic]);
        var handler = new DockerHostInspectExecutionHandler(inspector);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.DockerHostInspect,
            TargetResourceId = dockerHost.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "docker.host:local:inspect:1",
            TargetResourceSnapshot = dockerHost,
            ResourceSnapshot = [dockerHost]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        Assert.Same(dockerHost, Assert.Single(inspector.InspectInvocations));
    }

    [Fact]
    public async Task ContainerHostInspectOperation_DispatchesInspectInstruction()
    {
        var containerHost = CreateGraphResource("cloudshell.container-host:local", "local", revision: 49);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new ContainerHostInspectOperation(
            new ResourceProjectionExecutionContext(containerHost, [containerHost]),
            new ResourceOperationResolution(
                ContainerHostResourceTypeProvider.Operations.Inspect,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.ContainerHostInspect, request.InstructionType);
        Assert.Equal(containerHost.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(49, request.DesiredGeneration);
        Assert.Equal(
            $"{containerHost.EffectiveResourceId}:{ContainerHostResourceTypeProvider.Operations.Inspect}:49",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.RuntimeObservation, request.RequiredCapabilities);
        Assert.Same(containerHost, request.TargetResourceSnapshot);
        Assert.Equal([containerHost], request.ResourceSnapshot);
    }

    [Fact]
    public async Task ContainerHostInspectHandler_ReturnsInspectorDiagnostics()
    {
        var containerHost = CreateGraphResource("cloudshell.container-host:local", "local");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "containerHost.inspect.test",
            "Container host inspected.",
            containerHost.EffectiveResourceId);
        var inspector = new RecordingContainerHostInspector([diagnostic]);
        var handler = new ContainerHostInspectExecutionHandler(inspector);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ContainerHostInspect,
            TargetResourceId = containerHost.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "cloudshell.container-host:local:inspect:1",
            TargetResourceSnapshot = containerHost,
            ResourceSnapshot = [containerHost]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        Assert.Same(containerHost, Assert.Single(inspector.InspectInvocations));
    }

    [Fact]
    public async Task StorageInspectOperation_DispatchesInspectInstruction()
    {
        var storage = CreateGraphResource("cloudshell.storage:local", "local", revision: 53);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new StorageInspectOperation(
            new ResourceProjectionExecutionContext(storage, [storage]),
            new ResourceOperationResolution(
                StorageResourceTypeProvider.Operations.Inspect,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.StorageInspect, request.InstructionType);
        Assert.Equal(storage.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(53, request.DesiredGeneration);
        Assert.Equal(
            $"{storage.EffectiveResourceId}:{StorageResourceTypeProvider.Operations.Inspect}:53",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.RuntimeObservation, request.RequiredCapabilities);
        Assert.Same(storage, request.TargetResourceSnapshot);
        Assert.Equal([storage], request.ResourceSnapshot);
    }

    [Fact]
    public async Task StorageInspectHandler_ReturnsInspectorDiagnostics()
    {
        var storage = CreateGraphResource("cloudshell.storage:local", "local");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "storage.inspect.test",
            "Storage inspected.",
            storage.EffectiveResourceId);
        var inspector = new RecordingStorageInspector([diagnostic]);
        var handler = new StorageInspectExecutionHandler(inspector);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.StorageInspect,
            TargetResourceId = storage.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "cloudshell.storage:local:inspect:1",
            TargetResourceSnapshot = storage,
            ResourceSnapshot = [storage]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        Assert.Same(storage, Assert.Single(inspector.InspectInvocations));
    }

    [Fact]
    public async Task StorageInspectHandler_FailsWhenInspectorIsMissing()
    {
        var storage = CreateGraphResource("cloudshell.storage:local", "local");
        var handler = new StorageInspectExecutionHandler();
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.StorageInspect,
            TargetResourceId = storage.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "cloudshell.storage:local:inspect:1",
            TargetResourceSnapshot = storage,
            ResourceSnapshot = [storage]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("storage.inspectorMissing", diagnostic.Code);
        Assert.Equal(storage.EffectiveResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task HostConfigurationSourceInspectOperation_DispatchesInspectInstruction()
    {
        var source = CreateGraphResource("configuration.host:local", "local", revision: 59);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new HostConfigurationSourceInspectOperation(
            new ResourceProjectionExecutionContext(source, [source]),
            new ResourceOperationResolution(
                HostConfigurationSourceResourceTypeProvider.Operations.Inspect,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.HostConfigurationSourceInspect, request.InstructionType);
        Assert.Equal(source.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(59, request.DesiredGeneration);
        Assert.Equal(
            $"{source.EffectiveResourceId}:{HostConfigurationSourceResourceTypeProvider.Operations.Inspect}:59",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.RuntimeObservation, request.RequiredCapabilities);
        Assert.Same(source, request.TargetResourceSnapshot);
        Assert.Equal([source], request.ResourceSnapshot);
    }

    [Fact]
    public async Task HostConfigurationSourceInspectHandler_ReturnsInspectorDiagnostics()
    {
        var source = CreateGraphResource("configuration.host:local", "local");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "hostConfigurationSource.inspect.test",
            "Host configuration source inspected.",
            source.EffectiveResourceId);
        var inspector = new RecordingHostConfigurationSourceInspector([diagnostic]);
        var handler = new HostConfigurationSourceInspectExecutionHandler(inspector);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.HostConfigurationSourceInspect,
            TargetResourceId = source.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "configuration.host:local:inspect:1",
            TargetResourceSnapshot = source,
            ResourceSnapshot = [source]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        Assert.Same(source, Assert.Single(inspector.InspectInvocations));
    }

    [Fact]
    public async Task ConfigurationStoreInspectOperation_DispatchesInspectInstruction()
    {
        var configurationStore = CreateGraphResource("configuration.store:settings", "settings", revision: 61);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new ConfigurationStoreInspectOperation(
            new ResourceProjectionExecutionContext(configurationStore, [configurationStore]),
            new ResourceOperationResolution(
                ConfigurationStoreResourceTypeProvider.Operations.Inspect,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.ConfigurationStoreInspect, request.InstructionType);
        Assert.Equal(configurationStore.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(61, request.DesiredGeneration);
        Assert.Equal(
            $"{configurationStore.EffectiveResourceId}:{ConfigurationStoreResourceTypeProvider.Operations.Inspect}:61",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.RuntimeObservation, request.RequiredCapabilities);
        Assert.Same(configurationStore, request.TargetResourceSnapshot);
        Assert.Equal([configurationStore], request.ResourceSnapshot);
    }

    [Fact]
    public async Task ConfigurationStoreInspectHandler_ReturnsInspectorDiagnostics()
    {
        var configurationStore = CreateGraphResource("configuration.store:settings", "settings");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "configurationStore.inspect.test",
            "Configuration Store inspected.",
            configurationStore.EffectiveResourceId);
        var inspector = new RecordingConfigurationStoreInspector([diagnostic]);
        var handler = new ConfigurationStoreInspectExecutionHandler(inspector);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ConfigurationStoreInspect,
            TargetResourceId = configurationStore.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "configuration.store:settings:inspect:1",
            TargetResourceSnapshot = configurationStore,
            ResourceSnapshot = [configurationStore]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        Assert.Same(configurationStore, Assert.Single(inspector.InspectInvocations));
    }

    [Fact]
    public async Task SecretsVaultInspectOperation_DispatchesInspectInstruction()
    {
        var secretsVault = CreateGraphResource("secrets.vault:secrets", "secrets", revision: 67);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new SecretsVaultInspectOperation(
            new ResourceProjectionExecutionContext(secretsVault, [secretsVault]),
            new ResourceOperationResolution(
                SecretsVaultResourceTypeProvider.Operations.Inspect,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.SecretsVaultInspect, request.InstructionType);
        Assert.Equal(secretsVault.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(67, request.DesiredGeneration);
        Assert.Equal(
            $"{secretsVault.EffectiveResourceId}:{SecretsVaultResourceTypeProvider.Operations.Inspect}:67",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.RuntimeObservation, request.RequiredCapabilities);
        Assert.Same(secretsVault, request.TargetResourceSnapshot);
        Assert.Equal([secretsVault], request.ResourceSnapshot);
    }

    [Fact]
    public async Task SecretsVaultInspectHandler_ReturnsInspectorDiagnostics()
    {
        var secretsVault = CreateGraphResource("secrets.vault:secrets", "secrets");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "secretsVault.inspect.test",
            "Secrets Vault inspected.",
            secretsVault.EffectiveResourceId);
        var inspector = new RecordingSecretsVaultInspector([diagnostic]);
        var handler = new SecretsVaultInspectExecutionHandler(inspector);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.SecretsVaultInspect,
            TargetResourceId = secretsVault.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "secrets.vault:secrets:inspect:1",
            TargetResourceSnapshot = secretsVault,
            ResourceSnapshot = [secretsVault]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        Assert.Same(secretsVault, Assert.Single(inspector.InspectInvocations));
    }

    [Fact]
    public async Task ContainerApplicationLifecycleOperation_DispatchesLifecycleInstruction()
    {
        var containerApp = CreateGraphResource("container-app:orders", "orders", revision: 9);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new ContainerApplicationLifecycleOperation(
            new ResourceProjectionExecutionContext(containerApp, [containerApp]),
            new ResourceOperationResolution(
                ContainerApplicationResourceTypeProvider.Operations.Start,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.ContainerApplicationStart, request.InstructionType);
        Assert.Equal(containerApp.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(9, request.DesiredGeneration);
        Assert.Equal(
            $"{containerApp.EffectiveResourceId}:{ContainerApplicationResourceTypeProvider.Operations.Start}:9",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.Containers, request.RequiredCapabilities);
        Assert.Same(containerApp, request.TargetResourceSnapshot);
        Assert.Equal([containerApp], request.ResourceSnapshot);
    }

    [Fact]
    public async Task ContainerApplicationLifecycleHandler_ReturnsRuntimeDiagnostics()
    {
        var containerApp = CreateGraphResource("container-app:orders", "orders");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "containerApplication.lifecycle.test",
            "Container Application lifecycle applied.",
            containerApp.EffectiveResourceId);
        var runtimeHandler = new RecordingContainerApplicationRuntimeHandler([diagnostic]);
        var handler = new ContainerApplicationStartExecutionHandler(runtimeHandler);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ContainerApplicationStart,
            TargetResourceId = containerApp.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "container-app:orders:start:1",
            TargetResourceSnapshot = containerApp,
            ResourceSnapshot = [containerApp]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        var invocation = Assert.Single(runtimeHandler.LifecycleInvocations);
        Assert.Same(containerApp, invocation.Resource);
        Assert.Equal(ContainerApplicationResourceTypeProvider.Operations.Start, invocation.OperationId);
    }

    [Fact]
    public async Task ContainerApplicationImageUpdateOperation_DispatchesImageApplyInstruction()
    {
        var containerApp = CreateGraphResource("container-app:orders", "orders", revision: 10);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new ContainerApplicationImageUpdateOperation(
            new ResourceProjectionExecutionContext(containerApp, [containerApp]),
            new ResourceOperationResolution(
                ContainerApplicationResourceTypeProvider.Operations.UpdateImage,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.ContainerApplicationImageApply, request.InstructionType);
        Assert.Equal(containerApp.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(10, request.DesiredGeneration);
        Assert.Equal(
            $"{containerApp.EffectiveResourceId}:{ContainerApplicationResourceTypeProvider.Operations.UpdateImage}:10",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.Containers, request.RequiredCapabilities);
        Assert.Same(containerApp, request.TargetResourceSnapshot);
        Assert.Equal([containerApp], request.ResourceSnapshot);
    }

    [Fact]
    public async Task ContainerApplicationReplicasUpdateOperation_DispatchesReplicasApplyInstruction()
    {
        var containerApp = CreateGraphResource("container-app:orders", "orders", revision: 11);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new ContainerApplicationReplicasUpdateOperation(
            new ResourceProjectionExecutionContext(containerApp, [containerApp]),
            new ResourceOperationResolution(
                ContainerApplicationResourceTypeProvider.Operations.UpdateReplicas,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.ContainerApplicationReplicasApply, request.InstructionType);
        Assert.Equal(containerApp.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(11, request.DesiredGeneration);
        Assert.Equal(
            $"{containerApp.EffectiveResourceId}:{ContainerApplicationResourceTypeProvider.Operations.UpdateReplicas}:11",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.Containers, request.RequiredCapabilities);
        Assert.Same(containerApp, request.TargetResourceSnapshot);
        Assert.Equal([containerApp], request.ResourceSnapshot);
    }

    [Fact]
    public async Task ContainerApplicationImageApplyHandler_ReturnsRuntimeDiagnostics()
    {
        var containerApp = CreateGraphResource("container-app:orders", "orders");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "containerApplication.image.test",
            "Container Application image applied.",
            containerApp.EffectiveResourceId);
        var runtimeHandler = new RecordingContainerApplicationRuntimeHandler([diagnostic]);
        var handler = new ContainerApplicationImageApplyExecutionHandler(runtimeHandler);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ContainerApplicationImageApply,
            TargetResourceId = containerApp.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "container-app:orders:image:1",
            TargetResourceSnapshot = containerApp,
            ResourceSnapshot = [containerApp]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        Assert.Same(containerApp, Assert.Single(runtimeHandler.ImageInvocations));
    }

    [Fact]
    public async Task ContainerApplicationReplicasApplyHandler_ReturnsRuntimeDiagnostics()
    {
        var containerApp = CreateGraphResource("container-app:orders", "orders");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "containerApplication.replicas.test",
            "Container Application replicas applied.",
            containerApp.EffectiveResourceId);
        var runtimeHandler = new RecordingContainerApplicationRuntimeHandler([diagnostic]);
        var handler = new ContainerApplicationReplicasApplyExecutionHandler(runtimeHandler);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ContainerApplicationReplicasApply,
            TargetResourceId = containerApp.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "container-app:orders:replicas:1",
            TargetResourceSnapshot = containerApp,
            ResourceSnapshot = [containerApp]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        Assert.Same(containerApp, Assert.Single(runtimeHandler.ReplicaInvocations));
    }

    [Fact]
    public async Task ContainerApplicationRoutingReconcileHandler_ReturnsRuntimeDiagnostics()
    {
        var containerApp = CreateGraphResource("container-app:orders", "orders");
        var service = CreateContainerApplicationOrchestratorService(containerApp);
        var replicaGroup = RMResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "containerApplication.routing.test",
            "Container Application routing reconciled.",
            containerApp.EffectiveResourceId);
        var runtimeHandler = new RecordingContainerApplicationOrchestratorRuntimeHandler([diagnostic]);
        var handler = new ContainerApplicationRoutingReconcileExecutionHandler(runtimeHandler);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ContainerApplicationRoutingReconcile,
            TargetResourceId = containerApp.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "container-app:orders:routing:1",
            TargetResourceSnapshot = containerApp,
            ResourceSnapshot = [containerApp],
            Payload = JsonSerializer.SerializeToElement(
                new ContainerApplicationOrchestratorServiceExecutionPayload(
                    service,
                    replicaGroup,
                    []))
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        Assert.Equal("0", result.Observations["routingBindingCount"]);
        Assert.Equal("true", result.Observations["hasReplicaGroup"]);
        var invocation = Assert.Single(runtimeHandler.RoutingInvocations);
        Assert.Same(containerApp, invocation.Resource);
        Assert.Equal(service, invocation.Service);
        Assert.NotNull(invocation.ReplicaGroup);
        Assert.Equal(replicaGroup.Id, invocation.ReplicaGroup.Id);
        Assert.Equal(replicaGroup.ServiceId, invocation.ReplicaGroup.ServiceId);
        Assert.Equal(replicaGroup.RequestedReplicaSlots, invocation.ReplicaGroup.RequestedReplicaSlots);
        Assert.Equal(replicaGroup.Instances.Count, invocation.ReplicaGroup.Instances.Count);
        Assert.Empty(invocation.RoutingBindings);
    }

    [Fact]
    public async Task ContainerApplicationOrchestratorServicePrepareHandler_ReturnsRuntimeDiagnostics()
    {
        var containerApp = CreateGraphResource("container-app:orders", "orders");
        var service = CreateContainerApplicationOrchestratorService(containerApp);
        var replicaGroup = RMResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "containerApplication.prepare.test",
            "Container Application service prepared.",
            containerApp.EffectiveResourceId);
        var runtimeHandler = new RecordingContainerApplicationOrchestratorRuntimeHandler([diagnostic]);
        var handler = new ContainerApplicationOrchestratorServicePrepareExecutionHandler(runtimeHandler);
        var request = CreateContainerApplicationOrchestratorServiceRequest(
            containerApp,
            ProviderExecutionInstructionTypes.ContainerApplicationOrchestratorServicePrepare,
            service,
            replicaGroup);

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        var invocation = Assert.Single(runtimeHandler.PrepareInvocations);
        Assert.Same(containerApp, invocation.Resource);
        Assert.Equal(service, invocation.Service);
        Assert.NotNull(invocation.ReplicaGroup);
        Assert.Equal(replicaGroup.Id, invocation.ReplicaGroup.Id);
    }

    [Fact]
    public async Task ContainerApplicationRoutingTearDownHandler_ReturnsRuntimeDiagnostics()
    {
        var containerApp = CreateGraphResource("container-app:orders", "orders");
        var service = CreateContainerApplicationOrchestratorService(containerApp);
        var replicaGroup = RMResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "containerApplication.routingTeardown.test",
            "Container Application routing torn down.",
            containerApp.EffectiveResourceId);
        var runtimeHandler = new RecordingContainerApplicationOrchestratorRuntimeHandler([diagnostic]);
        var handler = new ContainerApplicationRoutingTearDownExecutionHandler(runtimeHandler);
        var request = CreateContainerApplicationOrchestratorServiceRequest(
            containerApp,
            ProviderExecutionInstructionTypes.ContainerApplicationRoutingTearDown,
            service,
            replicaGroup);

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        var invocation = Assert.Single(runtimeHandler.TearDownInvocations);
        Assert.Same(containerApp, invocation.Resource);
        Assert.Equal(service, invocation.Service);
        Assert.NotNull(invocation.ReplicaGroup);
        Assert.Equal(replicaGroup.Id, invocation.ReplicaGroup.Id);
    }

    [Fact]
    public async Task ContainerApplicationRoutingReconcileHandler_RequiresPayload()
    {
        var containerApp = CreateGraphResource("container-app:orders", "orders");
        var runtimeHandler = new RecordingContainerApplicationOrchestratorRuntimeHandler();
        var handler = new ContainerApplicationRoutingReconcileExecutionHandler(runtimeHandler);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ContainerApplicationRoutingReconcile,
            TargetResourceId = containerApp.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "container-app:orders:routing:1",
            TargetResourceSnapshot = containerApp,
            ResourceSnapshot = [containerApp]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Unavailable, result.Status);
        Assert.Empty(runtimeHandler.RoutingInvocations);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ProviderExecutionDiagnosticCodes.PayloadMissing);
    }

    [Fact]
    public async Task ContainerApplicationServiceInstanceStartHandler_ReturnsRuntimeDiagnostics()
    {
        var containerApp = CreateGraphResource("container-app:orders", "orders");
        var service = CreateContainerApplicationOrchestratorService(containerApp);
        var replicaGroup = RMResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);
        var instance = replicaGroup.Instances.First();
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "containerApplication.instance.start.test",
            "Container Application service instance started.",
            containerApp.EffectiveResourceId);
        var runtimeHandler = new RecordingContainerApplicationOrchestratorRuntimeHandler([diagnostic]);
        var handler = new ContainerApplicationServiceInstanceStartExecutionHandler(runtimeHandler);
        var request = CreateContainerApplicationServiceInstanceRequest(
            containerApp,
            ProviderExecutionInstructionTypes.ContainerApplicationServiceInstanceStart,
            service,
            instance,
            RMResourceAction.Start,
            replicaGroup);

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        Assert.Equal("Start", result.Observations["actionKind"]);
        Assert.Equal(instance.ReplicaOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture), result.Observations["replicaOrdinal"]);
        var invocation = Assert.Single(runtimeHandler.InstanceInvocations);
        Assert.Same(containerApp, invocation.Resource);
        Assert.Equal(service, invocation.Service);
        Assert.Equal(instance, invocation.Instance);
        Assert.Equal(RMResourceAction.Start, invocation.Action);
        Assert.NotNull(invocation.ReplicaGroup);
        Assert.Equal(replicaGroup.Id, invocation.ReplicaGroup.Id);
    }

    [Fact]
    public async Task ContainerApplicationServiceInstanceStopHandler_ReturnsRuntimeDiagnostics()
    {
        var containerApp = CreateGraphResource("container-app:orders", "orders");
        var service = CreateContainerApplicationOrchestratorService(containerApp);
        var replicaGroup = RMResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);
        var instance = replicaGroup.Instances.First();
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "containerApplication.instance.stop.test",
            "Container Application service instance stopped.",
            containerApp.EffectiveResourceId);
        var runtimeHandler = new RecordingContainerApplicationOrchestratorRuntimeHandler([diagnostic]);
        var handler = new ContainerApplicationServiceInstanceStopExecutionHandler(runtimeHandler);
        var request = CreateContainerApplicationServiceInstanceRequest(
            containerApp,
            ProviderExecutionInstructionTypes.ContainerApplicationServiceInstanceStop,
            service,
            instance,
            RMResourceAction.Stop,
            replicaGroup);

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        Assert.Equal("Stop", result.Observations["actionKind"]);
        var invocation = Assert.Single(runtimeHandler.InstanceInvocations);
        Assert.Same(containerApp, invocation.Resource);
        Assert.Equal(service, invocation.Service);
        Assert.Equal(instance, invocation.Instance);
        Assert.Equal(RMResourceAction.Stop, invocation.Action);
    }

    [Fact]
    public async Task ContainerApplicationServiceInstanceHandler_RequiresMatchingActionKind()
    {
        var containerApp = CreateGraphResource("container-app:orders", "orders");
        var service = CreateContainerApplicationOrchestratorService(containerApp);
        var replicaGroup = RMResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);
        var runtimeHandler = new RecordingContainerApplicationOrchestratorRuntimeHandler();
        var handler = new ContainerApplicationServiceInstanceStartExecutionHandler(runtimeHandler);
        var request = CreateContainerApplicationServiceInstanceRequest(
            containerApp,
            ProviderExecutionInstructionTypes.ContainerApplicationServiceInstanceStart,
            service,
            replicaGroup.Instances.First(),
            RMResourceAction.Stop,
            replicaGroup);

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Unavailable, result.Status);
        Assert.Empty(runtimeHandler.InstanceInvocations);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ProviderExecutionDiagnosticCodes.PayloadInvalid);
    }

    [Fact]
    public async Task SqlServerLifecycleOperation_DispatchesLifecycleInstruction()
    {
        var sqlServer = CreateGraphResource("sql-server:app", "app", revision: 5);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new SqlServerLifecycleOperation(
            new ResourceProjectionExecutionContext(sqlServer, [sqlServer]),
            new ResourceOperationResolution(
                SqlServerResourceTypeProvider.Operations.Start,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            new RecordingSqlServerRuntimeHandler(),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.SqlServerStart, request.InstructionType);
        Assert.Equal(sqlServer.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(5, request.DesiredGeneration);
        Assert.Equal(
            $"{sqlServer.EffectiveResourceId}:{SqlServerResourceTypeProvider.Operations.Start}:5",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.Containers, request.RequiredCapabilities);
        Assert.Same(sqlServer, request.TargetResourceSnapshot);
        Assert.Equal([sqlServer], request.ResourceSnapshot);
    }

    [Fact]
    public async Task SqlServerLifecycleHandler_ReturnsRuntimeDiagnostics()
    {
        var sqlServer = CreateGraphResource("sql-server:app", "app");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "sqlServer.lifecycle.test",
            "SQL Server lifecycle applied.",
            sqlServer.EffectiveResourceId);
        var runtimeHandler = new RecordingSqlServerRuntimeHandler([diagnostic]);
        var handler = new SqlServerStartExecutionHandler(runtimeHandler);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.SqlServerStart,
            TargetResourceId = sqlServer.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "sql-server:app:start:1",
            TargetResourceSnapshot = sqlServer,
            ResourceSnapshot = [sqlServer]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        var invocation = Assert.Single(runtimeHandler.LifecycleInvocations);
        Assert.Same(sqlServer, invocation.Resource);
        Assert.Equal(SqlServerResourceTypeProvider.Operations.Start, invocation.OperationId);
    }

    [Fact]
    public async Task RabbitMQReconcileAccessOperation_DispatchesAccessReconcileInstructionWithGrants()
    {
        var rabbitMQ = CreateGraphResource("rabbitmq:broker", "broker", revision: 3);
        var grant = CreateRabbitMQGrant(rabbitMQ.EffectiveResourceId);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new RabbitMQReconcileAccessOperation(
            new ResourceProjectionExecutionContext(rabbitMQ, [rabbitMQ]),
            new ResourceOperationResolution(
                RabbitMQResourceTypeProvider.Operations.ReconcileAccess,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            dispatcher,
            new RecordingResourcePermissionGrantReader([grant]));

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.RabbitMQAccessReconcile, request.InstructionType);
        Assert.Equal(rabbitMQ.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(3, request.DesiredGeneration);
        Assert.Equal(
            $"{rabbitMQ.EffectiveResourceId}:{RabbitMQResourceTypeProvider.Operations.ReconcileAccess}:3",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.RabbitMQAccess, request.RequiredCapabilities);
        Assert.Same(rabbitMQ, request.TargetResourceSnapshot);
        Assert.Equal([rabbitMQ], request.ResourceSnapshot);
        Assert.NotNull(request.Payload);
        var payload = System.Text.Json.JsonSerializer.Deserialize<RabbitMQAccessReconcileExecutionPayload>(
            request.Payload.Value.GetRawText());
        Assert.NotNull(payload);
        var payloadGrant = Assert.Single(payload.Grants);
        Assert.Equal(grant, payloadGrant);
    }

    [Fact]
    public async Task RabbitMQAccessReconcileHandler_ReturnsReconcilerDiagnostics()
    {
        var rabbitMQ = CreateGraphResource("rabbitmq:broker", "broker");
        var grant = CreateRabbitMQGrant(rabbitMQ.EffectiveResourceId);
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "rabbitmq.test",
            "RabbitMQ access reconciled.",
            rabbitMQ.EffectiveResourceId);
        var reconciler = new RecordingRabbitMQAccessReconciler([diagnostic]);
        var handler = new RabbitMQAccessReconcileExecutionHandler(reconciler);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.RabbitMQAccessReconcile,
            TargetResourceId = rabbitMQ.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "rabbitmq:broker:1",
            TargetResourceSnapshot = rabbitMQ,
            ResourceSnapshot = [rabbitMQ],
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                new RabbitMQAccessReconcileExecutionPayload([grant]))
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        Assert.Equal([grant], reconciler.Grants);
    }

    [Fact]
    public async Task RabbitMQAccessReconcileHandler_FailsWhenReconcilerIsMissing()
    {
        var rabbitMQ = CreateGraphResource("rabbitmq:broker", "broker");
        var grant = CreateRabbitMQGrant(rabbitMQ.EffectiveResourceId);
        var handler = new RabbitMQAccessReconcileExecutionHandler();
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.RabbitMQAccessReconcile,
            TargetResourceId = rabbitMQ.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "rabbitmq:broker:1",
            TargetResourceSnapshot = rabbitMQ,
            ResourceSnapshot = [rabbitMQ],
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                new RabbitMQAccessReconcileExecutionPayload([grant]))
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("application.rabbitmq.accessReconcilerMissing", diagnostic.Code);
        Assert.Equal(rabbitMQ.EffectiveResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task RabbitMQLifecycleOperation_DispatchesLifecycleInstruction()
    {
        var rabbitMQ = CreateGraphResource("rabbitmq:broker", "broker", revision: 4);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new RabbitMQLifecycleOperation(
            new ResourceProjectionExecutionContext(rabbitMQ, [rabbitMQ]),
            new ResourceOperationResolution(
                RabbitMQResourceTypeProvider.Operations.Start,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            new RecordingRabbitMQRuntimeHandler(),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.RabbitMQStart, request.InstructionType);
        Assert.Equal(rabbitMQ.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(4, request.DesiredGeneration);
        Assert.Equal(
            $"{rabbitMQ.EffectiveResourceId}:{RabbitMQResourceTypeProvider.Operations.Start}:4",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.Containers, request.RequiredCapabilities);
        Assert.Same(rabbitMQ, request.TargetResourceSnapshot);
        Assert.Equal([rabbitMQ], request.ResourceSnapshot);
    }

    [Fact]
    public async Task RabbitMQLifecycleHandler_ReturnsRuntimeDiagnostics()
    {
        var rabbitMQ = CreateGraphResource("rabbitmq:broker", "broker");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "rabbitmq.lifecycle.test",
            "RabbitMQ lifecycle applied.",
            rabbitMQ.EffectiveResourceId);
        var runtimeHandler = new RecordingRabbitMQRuntimeHandler([diagnostic]);
        var handler = new RabbitMQStartExecutionHandler(runtimeHandler);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.RabbitMQStart,
            TargetResourceId = rabbitMQ.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "rabbitmq:broker:start:1",
            TargetResourceSnapshot = rabbitMQ,
            ResourceSnapshot = [rabbitMQ]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        var invocation = Assert.Single(runtimeHandler.LifecycleInvocations);
        Assert.Same(rabbitMQ, invocation.Resource);
        Assert.Equal(RabbitMQResourceTypeProvider.Operations.Start, invocation.OperationId);
    }

    [Fact]
    public async Task EventBrokerLifecycleOperation_DispatchesLifecycleInstruction()
    {
        var eventBroker = CreateGraphResource("event-broker:default", "default", revision: 6);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new EventBrokerLifecycleOperation(
            new ResourceProjectionExecutionContext(eventBroker, [eventBroker]),
            new ResourceOperationResolution(
                EventBrokerResourceTypeProvider.Operations.Start,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            new RecordingEventBrokerRuntimeController(),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.EventBrokerStart, request.InstructionType);
        Assert.Equal(eventBroker.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(6, request.DesiredGeneration);
        Assert.Equal(
            $"{eventBroker.EffectiveResourceId}:{EventBrokerResourceTypeProvider.Operations.Start}:6",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.Processes, request.RequiredCapabilities);
        Assert.Same(eventBroker, request.TargetResourceSnapshot);
        Assert.Equal([eventBroker], request.ResourceSnapshot);
    }

    [Fact]
    public async Task EventBrokerLifecycleHandler_ReturnsRuntimeDiagnostics()
    {
        var eventBroker = CreateGraphResource("event-broker:default", "default");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "eventBroker.lifecycle.test",
            "Event Broker lifecycle applied.",
            eventBroker.EffectiveResourceId);
        var runtimeController = new RecordingEventBrokerRuntimeController([diagnostic]);
        var handler = new EventBrokerStartExecutionHandler(runtimeController);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.EventBrokerStart,
            TargetResourceId = eventBroker.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "event-broker:default:start:1",
            TargetResourceSnapshot = eventBroker,
            ResourceSnapshot = [eventBroker]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        var invocation = Assert.Single(runtimeController.LifecycleInvocations);
        Assert.Same(eventBroker, invocation.Resource);
        Assert.Equal(EventBrokerResourceTypeProvider.Operations.Start, invocation.OperationId);
    }

    [Fact]
    public async Task ConfigurationStoreLifecycleOperation_DispatchesLifecycleInstruction()
    {
        var configurationStore = CreateGraphResource("configuration-store:default", "default", revision: 7);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new ConfigurationStoreLifecycleOperation(
            new ResourceProjectionExecutionContext(configurationStore, [configurationStore]),
            new ResourceOperationResolution(
                ConfigurationStoreResourceTypeProvider.Operations.Start,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            new RecordingConfigurationStoreRuntimeController(),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.ConfigurationStoreStart, request.InstructionType);
        Assert.Equal(configurationStore.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(7, request.DesiredGeneration);
        Assert.Equal(
            $"{configurationStore.EffectiveResourceId}:{ConfigurationStoreResourceTypeProvider.Operations.Start}:7",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.Processes, request.RequiredCapabilities);
        Assert.Same(configurationStore, request.TargetResourceSnapshot);
        Assert.Equal([configurationStore], request.ResourceSnapshot);
    }

    [Fact]
    public async Task ConfigurationStoreLifecycleHandler_ReturnsRuntimeDiagnostics()
    {
        var configurationStore = CreateGraphResource("configuration-store:default", "default");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "configurationStore.lifecycle.test",
            "Configuration Store lifecycle applied.",
            configurationStore.EffectiveResourceId);
        var runtimeController = new RecordingConfigurationStoreRuntimeController([diagnostic]);
        var handler = new ConfigurationStoreStartExecutionHandler(runtimeController);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.ConfigurationStoreStart,
            TargetResourceId = configurationStore.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "configuration-store:default:start:1",
            TargetResourceSnapshot = configurationStore,
            ResourceSnapshot = [configurationStore]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        var invocation = Assert.Single(runtimeController.LifecycleInvocations);
        Assert.Same(configurationStore, invocation.Resource);
        Assert.Equal(ConfigurationStoreResourceTypeProvider.Operations.Start, invocation.OperationId);
    }

    [Fact]
    public async Task SecretsVaultLifecycleOperation_DispatchesLifecycleInstruction()
    {
        var secretsVault = CreateGraphResource("secrets-vault:default", "default", revision: 8);
        var dispatcher = new RecordingExecutionDispatcher();
        var operation = new SecretsVaultLifecycleOperation(
            new ResourceProjectionExecutionContext(secretsVault, [secretsVault]),
            new ResourceOperationResolution(
                SecretsVaultResourceTypeProvider.Operations.Start,
                ResourceDefinitionJson.EmptyObject,
                ResourceDefinitionValueSource.TypeDefinition,
                IsEnabled: true,
                AllowOverride: false),
            new RecordingSecretsVaultRuntimeController(),
            dispatcher);

        await operation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.SecretsVaultStart, request.InstructionType);
        Assert.Equal(secretsVault.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(8, request.DesiredGeneration);
        Assert.Equal(
            $"{secretsVault.EffectiveResourceId}:{SecretsVaultResourceTypeProvider.Operations.Start}:8",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.Processes, request.RequiredCapabilities);
        Assert.Same(secretsVault, request.TargetResourceSnapshot);
        Assert.Equal([secretsVault], request.ResourceSnapshot);
    }

    [Fact]
    public async Task SecretsVaultLifecycleHandler_ReturnsRuntimeDiagnostics()
    {
        var secretsVault = CreateGraphResource("secrets-vault:default", "default");
        var diagnostic = new ResourceDefinitionDiagnostic(
            ResourceDefinitionDiagnosticSeverity.Information,
            "secretsVault.lifecycle.test",
            "Secrets Vault lifecycle applied.",
            secretsVault.EffectiveResourceId);
        var runtimeController = new RecordingSecretsVaultRuntimeController([diagnostic]);
        var handler = new SecretsVaultStartExecutionHandler(runtimeController);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = "assignment-1",
            InstructionType = ProviderExecutionInstructionTypes.SecretsVaultStart,
            TargetResourceId = secretsVault.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = "secrets-vault:default:start:1",
            TargetResourceSnapshot = secretsVault,
            ResourceSnapshot = [secretsVault]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Equal([diagnostic], result.Diagnostics);
        var invocation = Assert.Single(runtimeController.LifecycleInvocations);
        Assert.Same(secretsVault, invocation.Resource);
        Assert.Equal(SecretsVaultResourceTypeProvider.Operations.Start, invocation.OperationId);
    }

    private sealed class RecordingExecutionHandler(
        string operationType,
        IReadOnlyList<string> capabilities) : IProviderExecutionHandler
    {
        public string InstructionType { get; } = operationType;

        public IReadOnlyList<string> Capabilities { get; } = capabilities;

        public ProviderExecutionRequest? Request { get; private set; }

        public ValueTask<ProviderExecutionResult> ExecuteAsync(
            ProviderExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;

            return ValueTask.FromResult(ProviderExecutionResult.Succeeded(request));
        }
    }

    private sealed class ScopedExecutionHandlerState
    {
        public string ScopeId { get; } = Guid.NewGuid().ToString("N");
    }

    private sealed class ScopedExecutionHandler(
        ScopedExecutionHandlerState state) : IProviderExecutionHandler
    {
        public string InstructionType =>
            ProviderExecutionInstructionTypes.NetworkEndpointReconcile;

        public IReadOnlyList<string> Capabilities { get; } =
        [
            ProviderExecutionCapabilities.HostNetworking
        ];

        public ValueTask<ProviderExecutionResult> ExecuteAsync(
            ProviderExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ProviderExecutionResult.Succeeded(
                request,
                observations: new Dictionary<string, string>
                {
                    ["scopeId"] = state.ScopeId
                }));
    }

    private sealed class MismatchedAssignmentExecutionHandler(
        string instructionType,
        IReadOnlyList<string> capabilities,
        string assignmentId) : IProviderExecutionHandler
    {
        public string InstructionType { get; } = instructionType;

        public IReadOnlyList<string> Capabilities { get; } = capabilities;

        public ValueTask<ProviderExecutionResult> ExecuteAsync(
            ProviderExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ProviderExecutionResult
            {
                AssignmentId = assignmentId,
                Status = ProviderExecutionStatus.Succeeded,
                ObservedGeneration = request.DesiredGeneration
            });
    }

    private sealed class RecordingExecutionDispatcher : IProviderExecutionDispatcher
    {
        public List<ProviderExecutionRequest> Requests { get; } = [];

        public ValueTask<ProviderExecutionResult> ExecuteAsync(
            ProviderExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return ValueTask.FromResult(ProviderExecutionResult.Succeeded(request));
        }
    }

    private sealed class RecordingNetworkEndpointMappingReconciler(
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics) : INetworkEndpointMappingReconciler
    {
        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
            GraphResource resource,
            ResourceProjectionExecutionContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(diagnostics);
    }

    private sealed class RecordingDnsZoneNameMappingReconciler(
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics) : IDnsZoneNameMappingReconciler
    {
        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileNameMappingsAsync(
            GraphResource resource,
            ResourceProjectionExecutionContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(diagnostics);
    }

    private sealed class RecordingVirtualNetworkEndpointMappingReconciler(
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics) : IVirtualNetworkEndpointMappingReconciler
    {
        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
            GraphResource resource,
            ResourceProjectionExecutionContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(diagnostics);
    }

    private sealed class RecordingLocalHostNetworkEndpointMappingReconciler(
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics) : ILocalHostNetworkEndpointMappingReconciler
    {
        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
            GraphResource resource,
            ResourceProjectionExecutionContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(diagnostics);
    }

    private sealed class RecordingMacOSHostNetworkEndpointMappingReconciler(
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics) : IMacOSHostNetworkEndpointMappingReconciler
    {
        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
            GraphResource resource,
            ResourceProjectionExecutionContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(diagnostics);
    }

    private sealed class RecordingLocalVolumeProvisioner(
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics) : ILocalVolumeProvisioner
    {
        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ProvisionAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(diagnostics);
    }

    private sealed class RecordingSqlServerAccessReconciler(
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics) : ISqlServerAccessReconciler
    {
        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAccessAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(diagnostics);
    }

    private sealed class RecordingLoadBalancerConfigurationApplier(
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics) : ILoadBalancerConfigurationApplier
    {
        public GraphResource? Resource { get; private set; }

        public ResourceProjectionExecutionContext? Context { get; private set; }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyConfigurationAsync(
            GraphResource resource,
            ResourceProjectionExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            Resource = resource;
            Context = context;

            return ValueTask.FromResult(diagnostics);
        }
    }

    private sealed class RecordingCloudShellVolumeProvisioner(
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics) : ICloudShellVolumeProvisioner
    {
        public List<GraphResource> Invocations { get; } = [];

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ProvisionAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(resource);

            return ValueTask.FromResult(diagnostics);
        }
    }

    private sealed class RecordingExecutableApplicationRuntimeController(
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics) : IExecutableApplicationRuntimeController
    {
        public List<GraphResource> StartInvocations { get; } = [];

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StartAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default)
        {
            StartInvocations.Add(resource);

            return ValueTask.FromResult(diagnostics);
        }
    }

    private sealed class RecordingAspNetCoreProjectRuntimeController(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null,
        AspNetCoreProjectRuntimeStatus status = AspNetCoreProjectRuntimeStatus.Unknown)
        : IAspNetCoreProjectRuntimeController
    {
        public List<(GraphResource Resource, ResourceOperationId OperationId)> LifecycleInvocations { get; } = [];

        public AspNetCoreProjectRuntimeStatus GetStatus(GraphResource resource) =>
            status;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
            GraphResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleInvocations.Add((resource, operationId));

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingJavaScriptAppRuntimeController(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null,
        JavaScriptAppRuntimeStatus status = JavaScriptAppRuntimeStatus.Unknown)
        : IJavaScriptAppRuntimeController
    {
        public List<(GraphResource Resource, ResourceOperationId OperationId)> LifecycleInvocations { get; } = [];

        public JavaScriptAppRuntimeStatus GetStatus(GraphResource resource) =>
            status;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
            GraphResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleInvocations.Add((resource, operationId));

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingJavaAppRuntimeController(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null,
        JavaAppRuntimeStatus status = JavaAppRuntimeStatus.Unknown)
        : IJavaAppRuntimeController
    {
        public List<(GraphResource Resource, ResourceOperationId OperationId)> LifecycleInvocations { get; } = [];

        public JavaAppRuntimeStatus GetStatus(GraphResource resource) =>
            status;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
            GraphResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleInvocations.Add((resource, operationId));

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingGoAppRuntimeController(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null,
        GoAppRuntimeStatus status = GoAppRuntimeStatus.Unknown)
        : IGoAppRuntimeController
    {
        public List<(GraphResource Resource, ResourceOperationId OperationId)> LifecycleInvocations { get; } = [];

        public GoAppRuntimeStatus GetStatus(GraphResource resource) =>
            status;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
            GraphResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleInvocations.Add((resource, operationId));

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingPythonAppRuntimeController(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null,
        PythonAppRuntimeStatus status = PythonAppRuntimeStatus.Unknown)
        : IPythonAppRuntimeController
    {
        public List<(GraphResource Resource, ResourceOperationId OperationId)> LifecycleInvocations { get; } = [];

        public PythonAppRuntimeStatus GetStatus(GraphResource resource) =>
            status;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
            GraphResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleInvocations.Add((resource, operationId));

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingDeviceRegistryRuntimeController(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null,
        ResourceWebAppRuntimeStatus status = ResourceWebAppRuntimeStatus.Unknown)
        : IDeviceRegistryRuntimeController
    {
        public List<(GraphResource Resource, ResourceOperationId OperationId)> LifecycleInvocations { get; } = [];

        public ResourceWebAppRuntimeStatus GetStatus(GraphResource resource) =>
            status;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
            GraphResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleInvocations.Add((resource, operationId));

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingIdentityProvisioningSetupHandler(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null)
        : IIdentityProvisioningSetupHandler
    {
        public List<GraphResource> SetupInvocations { get; } = [];

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> SetupAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default)
        {
            SetupInvocations.Add(resource);

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingSqlDatabaseCreationHandler(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null)
        : ISqlDatabaseCreationHandler
    {
        public List<SqlDatabaseCreationContext> CreationInvocations { get; } = [];

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> EnsureCreatedAsync(
            SqlDatabaseCreationContext context,
            CancellationToken cancellationToken = default)
        {
            CreationInvocations.Add(context);

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingServiceReconciler(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null)
        : IServiceReconciler
    {
        public List<GraphResource> ReconcileInvocations { get; } = [];

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default)
        {
            ReconcileInvocations.Add(resource);

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingDockerHostInspector(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null)
        : IDockerHostInspector
    {
        public List<GraphResource> InspectInvocations { get; } = [];

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default)
        {
            InspectInvocations.Add(resource);

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingContainerHostInspector(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null)
        : IContainerHostInspector
    {
        public List<GraphResource> InspectInvocations { get; } = [];

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default)
        {
            InspectInvocations.Add(resource);

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingStorageInspector(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null)
        : IStorageInspector
    {
        public List<GraphResource> InspectInvocations { get; } = [];

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default)
        {
            InspectInvocations.Add(resource);

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingHostConfigurationSourceInspector(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null)
        : IHostConfigurationSourceInspector
    {
        public List<GraphResource> InspectInvocations { get; } = [];

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default)
        {
            InspectInvocations.Add(resource);

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingConfigurationStoreInspector(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null)
        : IConfigurationStoreInspector
    {
        public List<GraphResource> InspectInvocations { get; } = [];

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default)
        {
            InspectInvocations.Add(resource);

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingSecretsVaultInspector(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null)
        : ISecretsVaultInspector
    {
        public List<GraphResource> InspectInvocations { get; } = [];

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default)
        {
            InspectInvocations.Add(resource);

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingContainerApplicationRuntimeHandler(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null) : IContainerApplicationRuntimeHandler
    {
        public List<(GraphResource Resource, ResourceOperationId OperationId)> LifecycleInvocations { get; } = [];

        public List<GraphResource> ImageInvocations { get; } = [];

        public List<GraphResource> ReplicaInvocations { get; } = [];

        public ContainerApplicationRuntimeStatus GetStatus(GraphResource resource) =>
            ContainerApplicationRuntimeStatus.Unknown;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
            GraphResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleInvocations.Add((resource, operationId));

            return ValueTask.FromResult(diagnostics ?? []);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default)
        {
            ImageInvocations.Add(resource);

            return ValueTask.FromResult(diagnostics ?? []);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default)
        {
            ReplicaInvocations.Add(resource);

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingContainerApplicationOrchestratorRuntimeHandler(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null) : IContainerApplicationOrchestratorRuntimeHandler
    {
        public List<(
            GraphResource Resource,
            RMResourceOrchestratorService Service,
            RMResourceOrchestratorReplicaGroup? ReplicaGroup,
            IReadOnlyList<RMResourceOrchestratorServiceRoutingBindingDefinition> RoutingBindings)> RoutingInvocations { get; } = [];

        public List<(
            GraphResource Resource,
            RMResourceOrchestratorService Service,
            RMResourceOrchestratorReplicaGroup? ReplicaGroup,
            IReadOnlyList<RMResourceOrchestratorServiceRoutingBindingDefinition> RoutingBindings)> PrepareInvocations { get; } = [];

        public List<(
            GraphResource Resource,
            RMResourceOrchestratorService Service,
            RMResourceOrchestratorReplicaGroup? ReplicaGroup,
            IReadOnlyList<RMResourceOrchestratorServiceRoutingBindingDefinition> RoutingBindings)> TearDownInvocations { get; } = [];

        public List<(
            GraphResource Resource,
            RMResourceOrchestratorService Service,
            RMResourceOrchestratorServiceInstance Instance,
            RMResourceAction Action,
            RMResourceOrchestratorReplicaGroup? ReplicaGroup)> InstanceInvocations { get; } = [];

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> PrepareOrchestratorServiceAsync(
            GraphResource resource,
            RMResourceOrchestratorService service,
            RMResourceOrchestratorReplicaGroup? replicaGroup,
            IReadOnlyList<RMResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
            CancellationToken cancellationToken = default)
        {
            PrepareInvocations.Add((resource, service, replicaGroup, routingBindings));

            return ValueTask.FromResult(diagnostics ?? []);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileOrchestratorServiceRoutingAsync(
            GraphResource resource,
            RMResourceOrchestratorService service,
            RMResourceOrchestratorReplicaGroup? replicaGroup,
            IReadOnlyList<RMResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
            CancellationToken cancellationToken = default)
        {
            RoutingInvocations.Add((resource, service, replicaGroup, routingBindings));

            return ValueTask.FromResult(diagnostics ?? []);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> TearDownOrchestratorServiceRoutingAsync(
            GraphResource resource,
            RMResourceOrchestratorService service,
            RMResourceOrchestratorReplicaGroup? replicaGroup,
            IReadOnlyList<RMResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
            CancellationToken cancellationToken = default)
        {
            TearDownInvocations.Add((resource, service, replicaGroup, routingBindings));

            return ValueTask.FromResult(diagnostics ?? []);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteOrchestratorServiceInstanceAsync(
            GraphResource resource,
            RMResourceOrchestratorService service,
            RMResourceOrchestratorServiceInstance instance,
            RMResourceAction action,
            RMResourceOrchestratorReplicaGroup? replicaGroup,
            CancellationToken cancellationToken = default)
        {
            InstanceInvocations.Add((resource, service, instance, action, replicaGroup));

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingSqlServerRuntimeHandler(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null) : ISqlServerRuntimeHandler
    {
        public List<(GraphResource Resource, ResourceOperationId OperationId)> LifecycleInvocations { get; } = [];

        public SqlServerRuntimeStatus GetStatus(GraphResource resource) =>
            SqlServerRuntimeStatus.Unknown;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
            GraphResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleInvocations.Add((resource, operationId));

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingRabbitMQAccessReconciler(
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics) : IRabbitMQAccessReconciler
    {
        public IReadOnlyList<CloudShell.Abstractions.ResourceManager.ResourcePermissionGrant> Grants { get; private set; } =
            [];

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAccessAsync(
            GraphResource resource,
            IReadOnlyList<CloudShell.Abstractions.ResourceManager.ResourcePermissionGrant> grants,
            CancellationToken cancellationToken = default)
        {
            Grants = grants;

            return ValueTask.FromResult(diagnostics);
        }
    }

    private sealed class RecordingRabbitMQRuntimeHandler(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null) : IRabbitMQRuntimeHandler
    {
        public List<(GraphResource Resource, ResourceOperationId OperationId)> LifecycleInvocations { get; } = [];

        public RabbitMQRuntimeStatus GetStatus(GraphResource resource) =>
            RabbitMQRuntimeStatus.Unknown;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
            GraphResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleInvocations.Add((resource, operationId));

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingEventBrokerRuntimeController(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null) : IEventBrokerRuntimeController
    {
        public List<(GraphResource Resource, ResourceOperationId OperationId)> LifecycleInvocations { get; } = [];

        public ResourceWebAppRuntimeStatus GetStatus(GraphResource resource) =>
            ResourceWebAppRuntimeStatus.Unknown;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
            GraphResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleInvocations.Add((resource, operationId));

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingConfigurationStoreRuntimeController(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null) : IConfigurationStoreRuntimeController
    {
        public List<(GraphResource Resource, ResourceOperationId OperationId)> LifecycleInvocations { get; } = [];

        public ResourceWebAppRuntimeStatus GetStatus(GraphResource resource) =>
            ResourceWebAppRuntimeStatus.Unknown;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
            GraphResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleInvocations.Add((resource, operationId));

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingSecretsVaultRuntimeController(
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null) : ISecretsVaultRuntimeController
    {
        public List<(GraphResource Resource, ResourceOperationId OperationId)> LifecycleInvocations { get; } = [];

        public ResourceWebAppRuntimeStatus GetStatus(GraphResource resource) =>
            ResourceWebAppRuntimeStatus.Unknown;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
            GraphResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleInvocations.Add((resource, operationId));

            return ValueTask.FromResult(diagnostics ?? []);
        }
    }

    private sealed class RecordingResourcePermissionGrantReader(
        IReadOnlyList<CloudShell.Abstractions.ResourceManager.ResourcePermissionGrant> grants)
        : CloudShell.Abstractions.ResourceManager.IResourcePermissionGrantReader
    {
        public IReadOnlyList<CloudShell.Abstractions.ResourceManager.ResourcePermissionGrant> GetPermissionGrants() =>
            grants;
    }

    private static CloudShell.Abstractions.ResourceManager.ResourcePermissionGrant CreateRabbitMQGrant(
        string targetResourceId) =>
        new(
            new CloudShell.Abstractions.ResourceManager.ResourcePrincipalReference(
                CloudShell.Abstractions.ResourceManager.ResourcePrincipalKind.ResourceIdentity,
                "application:api",
                SourceResourceId: "application:api"),
            targetResourceId,
            CloudShell.Abstractions.Authorization.RabbitMQResourceOperationPermissions.ReconcileAccess);

    private static RMResourceOrchestratorService CreateContainerApplicationOrchestratorService(
        GraphResource resource) =>
        new(
            resource.EffectiveResourceId,
            resource.Name,
            new RMResourceWorkloadConfiguration(
                RMResourceWorkloadKind.ContainerImage,
                resource.Name,
                Image: "orders:1",
                Replicas: 2,
                ReplicasEnabled: true));

    private static ProviderExecutionRequest CreateProviderExecutionRequest(
        string assignmentId) =>
        new()
        {
            AssignmentId = assignmentId,
            InstructionType = ProviderExecutionInstructionTypes.NetworkEndpointReconcile,
            TargetResourceId = "network:private",
            DesiredGeneration = 1,
            IdempotencyKey = $"{assignmentId}:1",
            RequiredCapabilities =
            [
                ProviderExecutionCapabilities.HostNetworking
            ],
            ResourceSnapshot =
            [
                CreateGraphResource("network:private", "private")
            ]
        };

    private static ProviderExecutionRequest CreateContainerApplicationOrchestratorServiceRequest(
        GraphResource resource,
        string instructionType,
        RMResourceOrchestratorService service,
        RMResourceOrchestratorReplicaGroup? replicaGroup) =>
        new()
        {
            AssignmentId = "assignment-1",
            InstructionType = instructionType,
            TargetResourceId = resource.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = $"{resource.EffectiveResourceId}:{instructionType}:1",
            TargetResourceSnapshot = resource,
            ResourceSnapshot = [resource],
            Payload = JsonSerializer.SerializeToElement(
                new ContainerApplicationOrchestratorServiceExecutionPayload(
                    service,
                    replicaGroup,
                    []))
        };

    private static ProviderExecutionRequest CreateContainerApplicationServiceInstanceRequest(
        GraphResource resource,
        string instructionType,
        RMResourceOrchestratorService service,
        RMResourceOrchestratorServiceInstance instance,
        RMResourceAction action,
        RMResourceOrchestratorReplicaGroup? replicaGroup) =>
        new()
        {
            AssignmentId = "assignment-1",
            InstructionType = instructionType,
            TargetResourceId = resource.EffectiveResourceId,
            DesiredGeneration = 1,
            IdempotencyKey = $"{resource.EffectiveResourceId}:{instructionType}:{instance.ReplicaOrdinal}:1",
            TargetResourceSnapshot = resource,
            ResourceSnapshot = [resource],
            Payload = JsonSerializer.SerializeToElement(
                new ContainerApplicationServiceInstanceExecutionPayload(
                    service,
                    instance,
                    action,
                    replicaGroup))
        };

    private static GraphResource CreateGraphResource(
        string resourceId,
        string name,
        long revision = 0,
        IReadOnlyDictionary<ResourceAttributeId, string>? attributes = null,
        IReadOnlyList<ResourceReference>? dependsOn = null)
    {
        var classId = ResourceClassId.Create("test");
        var typeId = ResourceTypeId.Create("test.resource");
        var attributeValues = attributes?.ToDictionary(
            pair => pair.Key,
            pair => ResourceAttributeValue.String(pair.Value));
        var attributeSet = new ResourceAttributeSet(
            attributeValues?.Select(pair => new ResourceAttributeResolution(
                pair.Key,
                pair.Value,
                ResourceDefinitionValueSource.TypeDefinition)) ?? []);
        var capabilities = new ResourceCapabilitySet([]);
        var operations = new ResourceOperationSet([]);
        var resourceClass = new ResourceClass(
            new ResourceClassDefinition(classId),
            attributeSet,
            capabilities,
            operations);
        var resourceType = new ResourceType(
            new ResourceTypeDefinition(typeId, classId),
            resourceClass,
            attributeSet,
            capabilities,
            operations);

        return new GraphResource(
            new ResourceState(
                name,
                typeId,
                ResourceId: resourceId,
                Version: new ResourceRevision(revision).ToString(),
                DependsOn: dependsOn,
                Attributes: attributeValues is null ? null : new ResourceAttributeValueMap(attributeValues)),
            resourceClass,
            resourceType,
            attributeSet,
            capabilities,
            operations,
            []);
    }

    private static GraphResource CreateGraphResourceWithAttributeValues(
        string resourceId,
        string name,
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeValue> attributes)
    {
        var classId = ResourceClassId.Create("test");
        var typeId = ResourceTypeId.Create("test.resource");
        var attributeSet = new ResourceAttributeSet(
            attributes.Select(pair => new ResourceAttributeResolution(
                pair.Key,
                pair.Value,
                ResourceDefinitionValueSource.TypeDefinition)));
        var capabilities = new ResourceCapabilitySet([]);
        var operations = new ResourceOperationSet([]);
        var resourceClass = new ResourceClass(
            new ResourceClassDefinition(classId),
            attributeSet,
            capabilities,
            operations);
        var resourceType = new ResourceType(
            new ResourceTypeDefinition(typeId, classId),
            resourceClass,
            attributeSet,
            capabilities,
            operations);

        return new GraphResource(
            new ResourceState(
                name,
                typeId,
                ResourceId: resourceId,
                Attributes: new ResourceAttributeValueMap(attributes)),
            resourceClass,
            resourceType,
            attributeSet,
            capabilities,
            operations,
            []);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "cloudshell-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
