using System.Collections.ObjectModel;
using System.Text.Json;

namespace CloudShell.ControlPlane.Providers;

public interface IProviderExecutionDispatcher
{
    ValueTask<ProviderExecutionResult> ExecuteAsync(
        ProviderExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IProviderExecutionHandler
{
    string OperationType { get; }

    IReadOnlyList<string> Capabilities { get; }

    ValueTask<ProviderExecutionResult> ExecuteAsync(
        ProviderExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class InProcessProviderExecutionDispatcher(
    IEnumerable<IProviderExecutionHandler> handlers) : IProviderExecutionDispatcher
{
    private readonly IReadOnlyList<IProviderExecutionHandler> _handlers =
        handlers.ToArray();

    public ValueTask<ProviderExecutionResult> ExecuteAsync(
        ProviderExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var candidates = _handlers
            .Where(handler => string.Equals(
                handler.OperationType,
                request.OperationType,
                StringComparison.Ordinal))
            .ToArray();

        if (candidates.Length == 0)
        {
            return ValueTask.FromResult(Unavailable(
                request,
                ProviderExecutionDiagnosticCodes.HandlerMissing,
                $"No provider execution handler is registered for operation '{request.OperationType}'."));
        }

        var handler = candidates.FirstOrDefault(handler =>
            request.RequiredCapabilities.All(requiredCapability =>
                handler.Capabilities.Contains(requiredCapability, StringComparer.Ordinal)));

        if (handler is null)
        {
            return ValueTask.FromResult(Unavailable(
                request,
                ProviderExecutionDiagnosticCodes.RequiredCapabilityMissing,
                $"No provider execution handler for operation '{request.OperationType}' has all required capabilities."));
        }

        return handler.ExecuteAsync(request, cancellationToken);
    }

    private static ProviderExecutionResult Unavailable(
        ProviderExecutionRequest request,
        string code,
        string message) =>
        new()
        {
            AssignmentId = request.AssignmentId,
            Status = ProviderExecutionStatus.Unavailable,
            Diagnostics =
            [
                ResourceDefinitionDiagnostic.Error(
                    code,
                    message,
                    request.TargetResourceId)
            ]
        };
}

public sealed record ProviderExecutionRequest
{
    public required string AssignmentId { get; init; }

    public required string OperationType { get; init; }

    public required string TargetResourceId { get; init; }

    public required long DesiredGeneration { get; init; }

    public required string IdempotencyKey { get; init; }

    public IReadOnlyList<string> RequiredCapabilities { get; init; } = [];

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        ProviderExecutionDefaults.EmptyStringDictionary;

    public JsonElement? Payload { get; init; }

    public DateTimeOffset? RequestedAt { get; init; }
}

public sealed record ProviderExecutionResult
{
    public required string AssignmentId { get; init; }

    public required ProviderExecutionStatus Status { get; init; }

    public long? ObservedGeneration { get; init; }

    public IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics { get; init; } = [];

    public IReadOnlyDictionary<string, string> Observations { get; init; } =
        ProviderExecutionDefaults.EmptyStringDictionary;

    public DateTimeOffset? ObservedAt { get; init; }

    public static ProviderExecutionResult Succeeded(
        ProviderExecutionRequest request,
        long? observedGeneration = null,
        IReadOnlyDictionary<string, string>? observations = null,
        DateTimeOffset? observedAt = null) =>
        new()
        {
            AssignmentId = request.AssignmentId,
            Status = ProviderExecutionStatus.Succeeded,
            ObservedGeneration = observedGeneration ?? request.DesiredGeneration,
            Observations = observations ?? ProviderExecutionDefaults.EmptyStringDictionary,
            ObservedAt = observedAt
        };

    public static ProviderExecutionResult Failed(
        ProviderExecutionRequest request,
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics,
        long? observedGeneration = null,
        IReadOnlyDictionary<string, string>? observations = null,
        DateTimeOffset? observedAt = null) =>
        new()
        {
            AssignmentId = request.AssignmentId,
            Status = ProviderExecutionStatus.Failed,
            ObservedGeneration = observedGeneration,
            Diagnostics = diagnostics,
            Observations = observations ?? ProviderExecutionDefaults.EmptyStringDictionary,
            ObservedAt = observedAt
        };
}

public enum ProviderExecutionStatus
{
    Unknown = 0,
    Pending,
    Running,
    Succeeded,
    Failed,
    Degraded,
    Unavailable
}

public static class ProviderExecutionCapabilities
{
    public const string Containers = "containers";
    public const string Processes = "processes";
    public const string FileSystem = "filesystem";
    public const string VolumeMounts = "volumeMounts";
    public const string HostNetworking = "hostNetworking";
    public const string HostCommands = "hostCommands";
    public const string RuntimeObservation = "runtimeObservation";
}

public static class ProviderExecutionOperationTypes
{
    public const string ContainerRun = "container.run";
    public const string ContainerStop = "container.stop";
    public const string ProcessRun = "process.run";
    public const string ProcessStop = "process.stop";
    public const string FileSystemProvision = "filesystem.provision";
    public const string VolumeMountMaterialize = "volumeMount.materialize";
    public const string NetworkEndpointReconcile = "network.endpoint.reconcile";
    public const string DnsNameMappingReconcile = "dns.nameMapping.reconcile";
}

public static class ProviderExecutionDiagnosticCodes
{
    public const string HandlerMissing = "providerExecution.handlerMissing";
    public const string RequiredCapabilityMissing =
        "providerExecution.requiredCapabilityMissing";
}

file static class ProviderExecutionDefaults
{
    public static readonly IReadOnlyDictionary<string, string> EmptyStringDictionary =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal));
}
