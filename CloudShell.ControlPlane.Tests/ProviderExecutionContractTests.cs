using CloudShell.ControlPlane.Providers;

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
}
