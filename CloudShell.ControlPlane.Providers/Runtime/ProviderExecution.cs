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
    string InstructionType { get; }

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

        if (request.Target.Kind is not ProviderExecutionTargetKind.Default
            and not ProviderExecutionTargetKind.InProcess)
        {
            return ValueTask.FromResult(Unavailable(
                request,
                ProviderExecutionDiagnosticCodes.ExecutionTargetUnsupported,
                $"The in-process provider execution dispatcher cannot execute instruction '{request.InstructionType}' for target '{request.Target.Kind}'."));
        }

        var candidates = _handlers
            .Where(handler => string.Equals(
                handler.InstructionType,
                request.InstructionType,
                StringComparison.Ordinal))
            .ToArray();

        if (candidates.Length == 0)
        {
            return ValueTask.FromResult(Unavailable(
                request,
                ProviderExecutionDiagnosticCodes.HandlerMissing,
                $"No provider execution handler is registered for instruction '{request.InstructionType}'."));
        }

        var handler = candidates.FirstOrDefault(handler =>
            request.RequiredCapabilities.All(requiredCapability =>
                handler.Capabilities.Contains(requiredCapability, StringComparer.Ordinal)));

        if (handler is null)
        {
            return ValueTask.FromResult(Unavailable(
                request,
                ProviderExecutionDiagnosticCodes.RequiredCapabilityMissing,
                $"No provider execution handler for instruction '{request.InstructionType}' has all required capabilities."));
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

    public required string InstructionType { get; init; }

    public required string TargetResourceId { get; init; }

    public required long DesiredGeneration { get; init; }

    public required string IdempotencyKey { get; init; }

    public ProviderExecutionTarget Target { get; init; } = ProviderExecutionTarget.Default;

    public IReadOnlyList<string> RequiredCapabilities { get; init; } = [];

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        ProviderExecutionDefaults.EmptyStringDictionary;

    public Resource? TargetResourceSnapshot { get; init; }

    public IReadOnlyList<Resource> ResourceSnapshot { get; init; } = [];

    public JsonElement? Payload { get; init; }

    public DateTimeOffset? RequestedAt { get; init; }

    public ResourceProjectionExecutionContext? TryCreateProjectionExecutionContext()
    {
        var targetResource = TargetResourceSnapshot ??
            ResourceSnapshot.FirstOrDefault(resource => string.Equals(
                resource.EffectiveResourceId,
                TargetResourceId,
                StringComparison.OrdinalIgnoreCase));

        if (targetResource is null)
        {
            return null;
        }

        return new ResourceProjectionExecutionContext(
            targetResource,
            ResourceSnapshot.Count == 0 ? [targetResource] : ResourceSnapshot);
    }
}

public sealed record ProviderExecutionTarget
{
    public static ProviderExecutionTarget Default { get; } = new()
    {
        Kind = ProviderExecutionTargetKind.Default
    };

    public static ProviderExecutionTarget InProcess { get; } = new()
    {
        Kind = ProviderExecutionTargetKind.InProcess,
        TargetId = "local"
    };

    public required ProviderExecutionTargetKind Kind { get; init; }

    public string? TargetId { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        ProviderExecutionDefaults.EmptyStringDictionary;
}

public enum ProviderExecutionTargetKind
{
    Default = 0,
    InProcess,
    Agent
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
        IReadOnlyList<ResourceDefinitionDiagnostic>? diagnostics = null,
        IReadOnlyDictionary<string, string>? observations = null,
        DateTimeOffset? observedAt = null) =>
        new()
        {
            AssignmentId = request.AssignmentId,
            Status = ProviderExecutionStatus.Succeeded,
            ObservedGeneration = observedGeneration ?? request.DesiredGeneration,
            Diagnostics = diagnostics ?? [],
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

    public static ProviderExecutionResult Unavailable(
        ProviderExecutionRequest request,
        string code,
        string message,
        long? observedGeneration = null,
        IReadOnlyDictionary<string, string>? observations = null,
        DateTimeOffset? observedAt = null) =>
        new()
        {
            AssignmentId = request.AssignmentId,
            Status = ProviderExecutionStatus.Unavailable,
            ObservedGeneration = observedGeneration,
            Diagnostics =
            [
                ResourceDefinitionDiagnostic.Error(
                    code,
                    message,
                    request.TargetResourceId)
            ],
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
    public const string VirtualNetworking = "virtualNetworking";
    public const string DnsNameMappings = "dnsNameMappings";
    public const string SqlServerAccess = "sqlServerAccess";
    public const string RabbitMQAccess = "rabbitMQAccess";
    public const string HostCommands = "hostCommands";
    public const string RuntimeObservation = "runtimeObservation";
}

public static class ProviderExecutionInstructionTypes
{
    public const string ContainerStart = "container.start";
    public const string ContainerStop = "container.stop";
    public const string ContainerPause = "container.pause";
    public const string ContainerRestart = "container.restart";
    public const string ContainerUnpause = "container.unpause";
    public const string ProcessStart = "process.start";
    public const string ProcessStop = "process.stop";
    public const string ProcessRestart = "process.restart";
    public const string EventBrokerStart = "eventBroker.start";
    public const string EventBrokerStop = "eventBroker.stop";
    public const string EventBrokerRestart = "eventBroker.restart";
    public const string ConfigurationStoreStart = "configurationStore.start";
    public const string ConfigurationStoreStop = "configurationStore.stop";
    public const string ConfigurationStoreRestart = "configurationStore.restart";
    public const string SecretsVaultStart = "secretsVault.start";
    public const string SecretsVaultStop = "secretsVault.stop";
    public const string SecretsVaultRestart = "secretsVault.restart";
    public const string FileSystemProvision = "filesystem.provision";
    public const string VolumeMountMaterialize = "volumeMount.materialize";
    public const string NetworkEndpointReconcile = "network.endpoint.reconcile";
    public const string LocalHostNetworkEndpointReconcile = "hostNetwork.local.endpoint.reconcile";
    public const string MacOSHostNetworkEndpointReconcile = "hostNetwork.macos.endpoint.reconcile";
    public const string VirtualNetworkEndpointReconcile = "virtualNetwork.endpoint.reconcile";
    public const string DnsNameMappingReconcile = "dns.nameMapping.reconcile";
    public const string SqlServerAccessReconcile = "sqlServer.access.reconcile";
    public const string SqlServerStart = "sqlServer.start";
    public const string SqlServerStop = "sqlServer.stop";
    public const string SqlServerRestart = "sqlServer.restart";
    public const string RabbitMQAccessReconcile = "rabbitMQ.access.reconcile";
    public const string RabbitMQStart = "rabbitMQ.start";
    public const string RabbitMQStop = "rabbitMQ.stop";
    public const string RabbitMQRestart = "rabbitMQ.restart";
}

public static class ProviderExecutionDiagnosticCodes
{
    public const string HandlerMissing = "providerExecution.handlerMissing";
    public const string RequiredCapabilityMissing =
        "providerExecution.requiredCapabilityMissing";
    public const string ResourceSnapshotMissing = "providerExecution.resourceSnapshotMissing";
    public const string ExecutionTargetUnsupported =
        "providerExecution.executionTargetUnsupported";
}

file static class ProviderExecutionDefaults
{
    public static readonly IReadOnlyDictionary<string, string> EmptyStringDictionary =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal));
}
