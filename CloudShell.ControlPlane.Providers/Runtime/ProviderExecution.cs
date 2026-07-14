using System.Collections.Concurrent;
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

public interface IProviderExecutionObservationStore
{
    ValueTask RecordAsync(
        ProviderExecutionRequest request,
        ProviderExecutionResult result,
        CancellationToken cancellationToken = default);

    ValueTask<ProviderExecutionObservation?> GetAsync(
        string assignmentId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ProviderExecutionObservation>> ListAsync(
        CancellationToken cancellationToken = default);
}

public sealed class InProcessProviderExecutionDispatcher(
    IEnumerable<IProviderExecutionHandler> handlers,
    IProviderExecutionObservationStore? observations = null) : IProviderExecutionDispatcher
{
    private readonly IReadOnlyList<IProviderExecutionHandler> _handlers =
        handlers.ToArray();

    private readonly IProviderExecutionObservationStore? _observations = observations;

    public async ValueTask<ProviderExecutionResult> ExecuteAsync(
        ProviderExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationDiagnostics = ValidateRequest(request);
        if (validationDiagnostics.Count > 0)
        {
            var result = new ProviderExecutionResult
            {
                AssignmentId = request.AssignmentId ?? string.Empty,
                Status = ProviderExecutionStatus.Failed,
                Diagnostics = validationDiagnostics
            };

            return string.IsNullOrWhiteSpace(request.AssignmentId)
                ? result
                : await CompleteAsync(request, result, cancellationToken);
        }

        if (request.Target.Kind is not ProviderExecutionTargetKind.Default
            and not ProviderExecutionTargetKind.InProcess)
        {
            return await CompleteAsync(
                request,
                Unavailable(
                    request,
                    ProviderExecutionDiagnosticCodes.ExecutionTargetUnsupported,
                    $"The in-process provider execution dispatcher cannot execute instruction '{request.InstructionType}' for target '{request.Target.Kind}'."),
                cancellationToken);
        }

        var candidates = _handlers
            .Where(handler => string.Equals(
                handler.InstructionType,
                request.InstructionType,
                StringComparison.Ordinal))
            .ToArray();

        if (candidates.Length == 0)
        {
            return await CompleteAsync(
                request,
                Unavailable(
                    request,
                    ProviderExecutionDiagnosticCodes.HandlerMissing,
                    $"No provider execution handler is registered for instruction '{request.InstructionType}'."),
                cancellationToken);
        }

        var handler = candidates.FirstOrDefault(handler =>
            request.RequiredCapabilities.All(requiredCapability =>
                handler.Capabilities.Contains(requiredCapability, StringComparer.Ordinal)));

        if (handler is null)
        {
            return await CompleteAsync(
                request,
                Unavailable(
                    request,
                    ProviderExecutionDiagnosticCodes.RequiredCapabilityMissing,
                    $"No provider execution handler for instruction '{request.InstructionType}' has all required capabilities."),
                cancellationToken);
        }

        return await CompleteAsync(
            request,
            await handler.ExecuteAsync(request, cancellationToken),
            cancellationToken);
    }

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateRequest(
        ProviderExecutionRequest request)
    {
        List<ResourceDefinitionDiagnostic>? diagnostics = null;

        AddRequiredDiagnostic(
            string.IsNullOrWhiteSpace(request.AssignmentId),
            "Provider execution assignment id is required.");
        AddRequiredDiagnostic(
            string.IsNullOrWhiteSpace(request.InstructionType),
            "Provider execution instruction type is required.");
        AddRequiredDiagnostic(
            string.IsNullOrWhiteSpace(request.TargetResourceId),
            "Provider execution target resource id is required.");
        AddRequiredDiagnostic(
            string.IsNullOrWhiteSpace(request.IdempotencyKey),
            "Provider execution idempotency key is required.");

        if (request.DesiredGeneration < 0)
        {
            AddDiagnostic("Provider execution desired generation cannot be negative.");
        }

        if (request.RequiredCapabilities is null)
        {
            AddDiagnostic("Provider execution required capabilities cannot be null.");
        }
        else if (request.RequiredCapabilities.Any(string.IsNullOrWhiteSpace))
        {
            AddDiagnostic("Provider execution required capabilities cannot contain empty values.");
        }

        if (request.TargetResourceSnapshot is not null &&
            !string.IsNullOrWhiteSpace(request.TargetResourceId) &&
            !string.Equals(
                request.TargetResourceSnapshot.EffectiveResourceId,
                request.TargetResourceId,
                StringComparison.OrdinalIgnoreCase))
        {
            AddDiagnostic("Provider execution target resource snapshot must match the target resource id.");
        }

        if (request.ResourceSnapshot is null)
        {
            AddDiagnostic("Provider execution resource snapshot cannot be null.");
        }
        else if (request.ResourceSnapshot.Any(resource => resource is null))
        {
            AddDiagnostic("Provider execution resource snapshot cannot contain null resources.");
        }
        else if (request.ResourceSnapshot.Count > 0 &&
            !string.IsNullOrWhiteSpace(request.TargetResourceId) &&
            !request.ResourceSnapshot.Any(resource => string.Equals(
                resource.EffectiveResourceId,
                request.TargetResourceId,
                StringComparison.OrdinalIgnoreCase)))
        {
            AddDiagnostic("Provider execution resource snapshot must include the target resource when provided.");
        }

        return diagnostics ?? [];

        void AddRequiredDiagnostic(
            bool condition,
            string message)
        {
            if (condition)
            {
                AddDiagnostic(message);
            }
        }

        void AddDiagnostic(string message)
        {
            diagnostics ??= [];
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ProviderExecutionDiagnosticCodes.RequestInvalid,
                message,
                string.IsNullOrWhiteSpace(request.TargetResourceId)
                    ? null
                    : request.TargetResourceId));
        }
    }

    private async ValueTask<ProviderExecutionResult> CompleteAsync(
        ProviderExecutionRequest request,
        ProviderExecutionResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        result = ValidateResultCorrelation(request, result);

        if (_observations is not null)
        {
            await _observations.RecordAsync(request, result, cancellationToken);
        }

        return result;
    }

    private static ProviderExecutionResult ValidateResultCorrelation(
        ProviderExecutionRequest request,
        ProviderExecutionResult result)
    {
        if (string.Equals(
            request.AssignmentId,
            result.AssignmentId,
            StringComparison.Ordinal))
        {
            return result;
        }

        return ProviderExecutionResult.Failed(
            request,
            [
                ResourceDefinitionDiagnostic.Error(
                    ProviderExecutionDiagnosticCodes.ResultAssignmentMismatch,
                    $"Provider execution handler returned assignment id '{result.AssignmentId}' for instruction '{request.InstructionType}', but the requested assignment id was '{request.AssignmentId}'.",
                    request.TargetResourceId)
            ],
            observations: new Dictionary<string, string>
            {
                ["reportedAssignmentId"] = result.AssignmentId
            });
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

public sealed record ProviderExecutionObservation
{
    public required string AssignmentId { get; init; }

    public required string InstructionType { get; init; }

    public required string TargetResourceId { get; init; }

    public required long DesiredGeneration { get; init; }

    public required string IdempotencyKey { get; init; }

    public required ProviderExecutionTarget Target { get; init; }

    public IReadOnlyList<string> RequiredCapabilities { get; init; } = [];

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        ProviderExecutionDefaults.EmptyStringDictionary;

    public required ProviderExecutionStatus Status { get; init; }

    public long? ObservedGeneration { get; init; }

    public IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics { get; init; } = [];

    public IReadOnlyDictionary<string, string> Observations { get; init; } =
        ProviderExecutionDefaults.EmptyStringDictionary;

    public DateTimeOffset? RequestedAt { get; init; }

    public DateTimeOffset? ObservedAt { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    public static ProviderExecutionObservation From(
        ProviderExecutionRequest request,
        ProviderExecutionResult result,
        DateTimeOffset recordedAt) =>
        new()
        {
            AssignmentId = request.AssignmentId,
            InstructionType = request.InstructionType,
            TargetResourceId = request.TargetResourceId,
            DesiredGeneration = request.DesiredGeneration,
            IdempotencyKey = request.IdempotencyKey,
            Target = request.Target,
            RequiredCapabilities = request.RequiredCapabilities,
            Metadata = request.Metadata,
            Status = result.Status,
            ObservedGeneration = result.ObservedGeneration,
            Diagnostics = result.Diagnostics,
            Observations = result.Observations,
            RequestedAt = request.RequestedAt,
            ObservedAt = result.ObservedAt,
            RecordedAt = recordedAt
        };
}

public sealed class InMemoryProviderExecutionObservationStore :
    IProviderExecutionObservationStore
{
    private readonly ConcurrentDictionary<string, ProviderExecutionObservation> _observations =
        new(StringComparer.Ordinal);

    public ValueTask RecordAsync(
        ProviderExecutionRequest request,
        ProviderExecutionResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        cancellationToken.ThrowIfCancellationRequested();

        _observations[request.AssignmentId] = ProviderExecutionObservation.From(
            request,
            result,
            DateTimeOffset.UtcNow);

        return ValueTask.CompletedTask;
    }

    public ValueTask<ProviderExecutionObservation?> GetAsync(
        string assignmentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assignmentId);

        cancellationToken.ThrowIfCancellationRequested();

        _observations.TryGetValue(assignmentId, out var observation);

        return ValueTask.FromResult(observation);
    }

    public ValueTask<IReadOnlyList<ProviderExecutionObservation>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult<IReadOnlyList<ProviderExecutionObservation>>(
            _observations.Values
                .OrderBy(observation => observation.RecordedAt)
                .ThenBy(observation => observation.AssignmentId, StringComparer.Ordinal)
                .ToArray());
    }
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
        var resourceSnapshot = ResourceSnapshot ?? [];
        var targetResource =
            TargetResourceSnapshot is not null &&
            string.Equals(
                TargetResourceSnapshot.EffectiveResourceId,
                TargetResourceId,
                StringComparison.OrdinalIgnoreCase)
                ? TargetResourceSnapshot
                : resourceSnapshot.FirstOrDefault(resource => string.Equals(
                    resource.EffectiveResourceId,
                    TargetResourceId,
                    StringComparison.OrdinalIgnoreCase));

        if (targetResource is null)
        {
            return null;
        }

        if (resourceSnapshot.Count == 0)
        {
            return new ResourceProjectionExecutionContext(targetResource);
        }

        var resources = resourceSnapshot.Any(resource => string.Equals(
            resource.EffectiveResourceId,
            TargetResourceId,
            StringComparison.OrdinalIgnoreCase))
                ? resourceSnapshot
                : [targetResource, .. resourceSnapshot];

        return new ResourceProjectionExecutionContext(targetResource, resources);
    }
}

public static class ProviderExecutionRequests
{
    public static ProviderExecutionRequest CreateForResource(
        Resource resource,
        string executionKey,
        string instructionType,
        IReadOnlyList<string>? requiredCapabilities = null,
        IReadOnlyList<Resource>? resourceSnapshot = null,
        JsonElement? payload = null,
        ProviderExecutionTarget? target = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        long? desiredGeneration = null,
        DateTimeOffset? requestedAt = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructionType);

        var generation = desiredGeneration ?? resource.Revision.Value;

        return new ProviderExecutionRequest
        {
            AssignmentId = $"{resource.EffectiveResourceId}:{executionKey}",
            InstructionType = instructionType,
            TargetResourceId = resource.EffectiveResourceId,
            DesiredGeneration = generation,
            IdempotencyKey = $"{resource.EffectiveResourceId}:{executionKey}:{generation}",
            Target = target ?? ProviderExecutionTarget.Default,
            RequiredCapabilities = requiredCapabilities ?? [],
            Metadata = metadata ?? ProviderExecutionDefaults.EmptyStringDictionary,
            TargetResourceSnapshot = resource,
            ResourceSnapshot = resourceSnapshot ?? [resource],
            Payload = payload,
            RequestedAt = requestedAt ?? DateTimeOffset.UtcNow
        };
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
    public const string LoadBalancing = "loadBalancing";
    public const string Storage = "storage";
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
    public const string ContainerApplicationStart = "containerApplication.start";
    public const string ContainerApplicationStop = "containerApplication.stop";
    public const string ContainerApplicationRestart = "containerApplication.restart";
    public const string ContainerApplicationImageApply = "containerApplication.image.apply";
    public const string ContainerApplicationReplicasApply = "containerApplication.replicas.apply";
    public const string ContainerApplicationOrchestratorServicePrepare = "containerApplication.service.prepare";
    public const string ContainerApplicationRoutingReconcile = "containerApplication.routing.reconcile";
    public const string ContainerApplicationRoutingTearDown = "containerApplication.routing.tearDown";
    public const string ContainerApplicationServiceInstanceStart = "containerApplication.serviceInstance.start";
    public const string ContainerApplicationServiceInstanceStop = "containerApplication.serviceInstance.stop";
    public const string AspNetCoreProjectStart = "aspNetCoreProject.start";
    public const string AspNetCoreProjectStop = "aspNetCoreProject.stop";
    public const string AspNetCoreProjectRestart = "aspNetCoreProject.restart";
    public const string JavaScriptAppStart = "javaScriptApp.start";
    public const string JavaScriptAppStop = "javaScriptApp.stop";
    public const string JavaScriptAppRestart = "javaScriptApp.restart";
    public const string JavaAppStart = "javaApp.start";
    public const string JavaAppStop = "javaApp.stop";
    public const string JavaAppRestart = "javaApp.restart";
    public const string ExecutableApplicationStart = "executableApplication.start";
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
    public const string LoadBalancerConfigurationApply = "loadBalancer.configuration.apply";
    public const string StorageVolumeProvision = "storage.volume.provision";
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
    public const string RequestInvalid = "providerExecution.requestInvalid";
    public const string HandlerMissing = "providerExecution.handlerMissing";
    public const string RequiredCapabilityMissing =
        "providerExecution.requiredCapabilityMissing";
    public const string ResourceSnapshotMissing = "providerExecution.resourceSnapshotMissing";
    public const string PayloadMissing = "providerExecution.payloadMissing";
    public const string PayloadInvalid = "providerExecution.payloadInvalid";
    public const string ExecutionTargetUnsupported =
        "providerExecution.executionTargetUnsupported";
    public const string ResultAssignmentMismatch =
        "providerExecution.resultAssignmentMismatch";
}

file static class ProviderExecutionDefaults
{
    public static readonly IReadOnlyDictionary<string, string> EmptyStringDictionary =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal));
}
