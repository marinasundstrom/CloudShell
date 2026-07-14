using CloudShell.ControlPlane.Providers;
using CloudShell.ResourceModel;
using GraphResource = CloudShell.ResourceModel.Resource;

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
        Assert.Contains(ProviderExecutionCapabilities.Containers, request.RequiredCapabilities);
        Assert.Contains(ProviderExecutionCapabilities.VolumeMounts, request.RequiredCapabilities);
        Assert.Equal("local-docker", request.Metadata["provider"]);
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
            RequiredCapabilities = [ProviderExecutionCapabilities.Processes]
        };

        var result = await dispatcher.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        Assert.Same(request, handler.Request);
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

    private static GraphResource CreateGraphResource(
        string resourceId,
        string name,
        long revision = 0)
    {
        var classId = ResourceClassId.Create("test");
        var typeId = ResourceTypeId.Create("test.resource");
        var attributes = new ResourceAttributeSet([]);
        var capabilities = new ResourceCapabilitySet([]);
        var operations = new ResourceOperationSet([]);
        var resourceClass = new ResourceClass(
            new ResourceClassDefinition(classId),
            attributes,
            capabilities,
            operations);
        var resourceType = new ResourceType(
            new ResourceTypeDefinition(typeId, classId),
            resourceClass,
            attributes,
            capabilities,
            operations);

        return new GraphResource(
            new ResourceState(
                name,
                typeId,
                ResourceId: resourceId,
                Version: new ResourceRevision(revision).ToString()),
            resourceClass,
            resourceType,
            attributes,
            capabilities,
            operations,
            []);
    }
}
