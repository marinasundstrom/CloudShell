namespace CloudShell.ControlPlane.Providers;

public sealed class EventBrokerRuntimeOptions
{
    public string ServiceProjectPath { get; set; } =
        "CloudShell.EventBrokerService/CloudShell.EventBrokerService.csproj";

    public string? ServiceWorkingDirectory { get; set; }

    public string DefinitionsDirectory { get; set; } = Path.Combine(
        Path.GetTempPath(),
        "CloudShell.ResourceModel",
        "EventBroker");

    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public string? ServiceAuthenticationIssuer { get; set; }

    public string? ServiceAuthenticationAudience { get; set; }

    public string? ServiceAuthenticationSigningKeyPem { get; set; }
}

public interface IEventBrokerRuntimeController
{
    ResourceWebAppRuntimeStatus GetStatus(Resource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);
}

public interface IEventBrokerRuntimeMonitor
{
    ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        string resourceId,
        CancellationToken cancellationToken = default);
}

public sealed class EventBrokerProcessRuntimeController(
    EventBrokerRuntimeOptions? options = null) :
    IEventBrokerRuntimeController,
    IEventBrokerRuntimeMonitor,
    IDisposable,
    IAsyncDisposable
{
    private readonly EventBrokerRuntimeOptions _options =
        options ?? new EventBrokerRuntimeOptions();
    private readonly ResourceWebAppProcessRuntime _runtime = new();

    public ResourceWebAppRuntimeStatus GetStatus(Resource resource) =>
        _runtime.GetStatus(resource);

    public async ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        await _runtime.GetMonitoringSnapshotAsync(resourceId, cancellationToken);

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        await _runtime.ExecuteAsync(
            resource,
            operationId,
            EventBrokerResourceTypeProvider.Attributes.Endpoint,
            new ResourceWebAppProcessOptions(
                _options.ServiceProjectPath,
                "CloudShell__EventBrokerService__DefinitionsPath",
                "CloudShell__EventBrokerService__ResourceId",
                "event-brokers.json",
                _options.StartupTimeout)
            {
                ServiceWorkingDirectory = _options.ServiceWorkingDirectory,
                DefinitionsDirectory = _options.DefinitionsDirectory,
                EnvironmentVariables = CreateEnvironmentVariables(_options)
            },
            CreateDefinition,
            "event.broker",
            "Event Broker",
            cancellationToken);

    public async ValueTask DisposeAsync() =>
        await _runtime.DisposeAsync();

    public void Dispose() =>
        _runtime.Dispose();

    private static object CreateDefinition(
        Resource resource,
        string? endpoint) =>
        new
        {
            id = resource.EffectiveResourceId,
            name = resource.Name,
            displayName = resource.State.DisplayName,
            endpoint,
            protocols = resource.Attributes.GetObject<EventBrokerProtocolEndpoint[]>(
                EventBrokerResourceTypeProvider.Attributes.Protocols) ?? []
        };

    private static IReadOnlyDictionary<string, string> CreateEnvironmentVariables(
        EventBrokerRuntimeOptions options)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authentication__BuiltInAuthority__Enabled"] = "true"
        };

        AddIfNotWhiteSpace(
            variables,
            "Authentication__BuiltInAuthority__Issuer",
            options.ServiceAuthenticationIssuer);
        AddIfNotWhiteSpace(
            variables,
            "Authentication__BuiltInAuthority__Audience",
            options.ServiceAuthenticationAudience);
        AddIfNotWhiteSpace(
            variables,
            "Authentication__BuiltInAuthority__SigningKeyPem",
            options.ServiceAuthenticationSigningKeyPem);

        return variables;
    }

    private static void AddIfNotWhiteSpace(
        IDictionary<string, string> variables,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            variables[name] = value;
        }
    }
}

public sealed class NoopEventBrokerRuntimeController :
    IEventBrokerRuntimeController,
    IEventBrokerRuntimeMonitor
{
    public ResourceWebAppRuntimeStatus GetStatus(Resource resource) =>
        ResourceWebAppRuntimeStatus.Unknown;

    public ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ResourceProcessMonitoringSnapshot?>(null);

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            EventBrokerRuntimeReadiness.CreateMissingDiagnostic(resource, operationId)
        ]);
}

internal static class EventBrokerRuntimeReadiness
{
    public const string DiagnosticCode = "event.broker.runtimeControllerMissing";

    public static bool IsMissing(IEventBrokerRuntimeController? runtimeController) =>
        runtimeController is null or NoopEventBrokerRuntimeController;

    public static string CreateMissingReason(Resource resource, ResourceOperationId operationId) =>
        $"Event Broker resource '{resource.EffectiveResourceId}' cannot execute '{operationId}' because no Event Broker runtime controller is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(
        Resource resource,
        ResourceOperationId operationId) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource, operationId),
            resource.EffectiveResourceId);
}
