using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;

namespace CloudShell.ControlPlane.Providers;

public sealed class PythonAppResourceManagerStateProvider(
    IPythonAppRuntimeController? runtimeController = null) :
    IResourceModelResourceManagerStateProvider
{
    private readonly IPythonAppRuntimeController _runtimeController =
        runtimeController ?? new NoopPythonAppRuntimeController();

    public ResourceManagerState? GetState(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (resource.Type.TypeId != PythonAppResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        return _runtimeController.GetStatus(resource) switch
        {
            PythonAppRuntimeStatus.Running => ResourceManagerState.Running,
            PythonAppRuntimeStatus.Stopped => ResourceManagerState.Stopped,
            _ => null
        };
    }
}

public sealed class PythonAppResourceManagerEndpointProjectionProvider :
    IResourceModelResourceManagerEndpointProjectionProvider
{
    public ResourceModelResourceManagerEndpointProjection? GetEndpointProjection(
        Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (resource.Type.TypeId != PythonAppResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        var requests = resource.Attributes
            .GetObject<NetworkingEndpointRequestValue[]>(
                PythonAppResourceTypeProvider.Attributes.EndpointRequests) ?? [];

        if (requests.Length == 0)
        {
            return ResourceModelResourceManagerEndpointProjection.Empty;
        }

        var endpoints = requests
            .Where(request =>
                !string.IsNullOrWhiteSpace(request.Name) &&
                !string.IsNullOrWhiteSpace(request.Protocol))
            .Select(request => new ResourceEndpoint(
                request.Name.Trim(),
                NormalizeProtocol(request.Protocol),
                ParseExposure(request.Exposure),
                request.TargetPort ?? request.Port))
            .ToArray();
        var endpointNetworkMappings = requests
            .Select(request => CreateEndpointNetworkMapping(resource, request))
            .Where(mapping => mapping is not null)
            .Cast<ResourceEndpointNetworkMapping>()
            .ToArray();

        return endpoints.Length == 0 && endpointNetworkMappings.Length == 0
            ? ResourceModelResourceManagerEndpointProjection.Empty
            : new ResourceModelResourceManagerEndpointProjection(
                endpoints,
                EndpointNetworkMappings: endpointNetworkMappings);
    }

    private static ResourceEndpointNetworkMapping? CreateEndpointNetworkMapping(
        Resource resource,
        NetworkingEndpointRequestValue request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Protocol) ||
            request.Port is not > 0)
        {
            return null;
        }

        var host = FirstNonEmpty(request.Host, request.IpAddress);
        if (host is null)
        {
            return null;
        }

        var protocol = NormalizeProtocol(request.Protocol);
        var address = protocol is "http" or "https"
            ? $"{protocol}://{host}:{request.Port.Value}"
            : $"{host}:{request.Port.Value}";

        return ResourceEndpointNetworkMapping.ForEndpoint(
            resource.EffectiveResourceId,
            request.Name,
            address,
            ParseExposure(request.Exposure),
            request.Network?.TryGetResourceId(out var networkResourceId) == true
                ? networkResourceId
                : null);
    }

    private static string NormalizeProtocol(string protocol) =>
        protocol.Trim().ToLowerInvariant();

    private static ResourceExposureScope ParseExposure(string? exposure) =>
        !string.IsNullOrWhiteSpace(exposure) &&
        Enum.TryParse<ResourceExposureScope>(exposure, ignoreCase: true, out var parsed)
            ? parsed
            : ResourceExposureScope.Local;

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}

public sealed class PythonAppResourceManagerObservabilityProvider :
    IResourceModelResourceManagerObservabilityProvider
{
    public ResourceObservability? GetObservability(
        Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resource.Type.TypeId == PythonAppResourceTypeProvider.ResourceTypeId
            ? new ResourceObservability(
                Logs: true,
                Traces: true,
                Metrics: true,
                ServiceName: resource.Name)
            : null;
    }
}

public sealed class PythonAppResourceManagerMonitoringProvider(
    IPythonAppRuntimeMonitor? runtimeMonitor = null) : IResourceMonitoringProvider
{
    private const string ProviderDisplayName = "Python app";
    private readonly IPythonAppRuntimeMonitor _runtimeMonitor =
        runtimeMonitor ?? new NoopPythonAppRuntimeController();

    public bool CanMonitor(ResourceManagerResource resource) =>
        string.Equals(
            resource.EffectiveTypeId,
            PythonAppResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase);

    public async Task<ResourceMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        ResourceManagerResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!CanMonitor(resource))
        {
            return null;
        }

        var timestamp = DateTimeOffset.UtcNow;
        var snapshot = await _runtimeMonitor.GetMonitoringSnapshotAsync(
            resource.Id,
            cancellationToken);
        if (snapshot is null)
        {
            return new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderDisplayName,
                timestamp,
                [],
                "Unavailable",
                "The Python app process could not be observed.");
        }

        return new ResourceMonitoringSnapshot(
            resource.Id,
            ProviderDisplayName,
            snapshot.Timestamp,
            ResourceProcessMonitoringMetricSamples.Create(
                snapshot,
                "Python app process"),
            "Available",
            "Python app process metrics.");
    }
}

public sealed class PythonAppResourceManagerLogProvider(
    IPythonAppRuntimeOutputReader? outputReader = null) : ILogProvider
{
    private readonly IPythonAppRuntimeOutputReader _outputReader =
        outputReader ?? new NoopPythonAppRuntimeController();

    public string Id => "resource-model.python-app.logs";

    public string DisplayName => "Python app logs";

    public IReadOnlyList<LogSource> GetLogSources() => [];

    public bool CanOpenLogSource(LogSource source) =>
        IsPythonAppLogSource(source);

    public ValueTask<ILogSourceSession?> OpenLogSourceAsync(
        LogSource source,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult<ILogSourceSession?>(
            IsPythonAppLogSource(source) && source.ResourceId is not null
                ? new PythonAppResourceManagerLogSourceSession(_outputReader, source)
                : null);
    }

    public Task<IReadOnlyList<LogEntry>> ReadLogSourceAsync(
        string logSourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<LogEntry>>([]);
    }

    private static bool IsPythonAppLogSource(LogSource source) =>
        source.ResourceId?.StartsWith(
            $"{PythonAppResourceTypeProvider.ResourceTypeId}:",
            StringComparison.OrdinalIgnoreCase) == true &&
        source.Kind is ResourceLogSourceKind.ProcessOutput
            or ResourceLogSourceKind.ProcessStdout
            or ResourceLogSourceKind.ProcessStderr;
}

public sealed class PythonAppResourceManagerLogSourceSession(
    IPythonAppRuntimeOutputReader outputReader,
    LogSource source) : ILogSourceSession
{
    public string Id { get; } = Guid.NewGuid().ToString("N");

    public string SourceId => source.Id;

    public LogSourceSessionStatus Status { get; private set; } = LogSourceSessionStatus.Active;

    public Task<IReadOnlyList<LogEntry>> ReadAsync(
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<LogEntry> entries = source.ResourceId is null
            ? []
            : outputReader
                .ReadOutput(source.ResourceId, maxEntries, before)
                .Select(ToLogEntry)
                .ToArray();

        return Task.FromResult(entries);
    }

    public async IAsyncEnumerable<LogEntry> StreamAsync(
        int initialEntries = 50,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var entries = await ReadAsync(initialEntries, cancellationToken: cancellationToken);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    public ValueTask DisposeAsync()
    {
        Status = LogSourceSessionStatus.Closed;
        return ValueTask.CompletedTask;
    }

    private static LogEntry ToLogEntry(PythonAppRuntimeOutputEntry entry) =>
        new(
            entry.Timestamp,
            entry.Message,
            entry.Severity,
            entry.Stream);
}
