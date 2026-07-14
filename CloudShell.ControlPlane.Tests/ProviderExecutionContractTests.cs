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
            OperationType = ProviderExecutionOperationTypes.ContainerRun,
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
        Assert.Equal(ProviderExecutionOperationTypes.ContainerRun, request.OperationType);
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
            OperationType = ProviderExecutionOperationTypes.NetworkEndpointReconcile,
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
}
