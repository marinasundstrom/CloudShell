using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using Docker.DotNet;
using Docker.DotNet.Models;
using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CloudShell.Providers.Docker;

public sealed partial class DockerContainerResourceProvider :
    IResourceProvider,
    ILogProvider,
    IResourceProcedureProvider,
    IProgrammaticResourceDeclarationProvider,
    IResourceOrchestrationDescriptorProvider,
    IDisposable
{
    private static readonly JsonSerializerOptions DescriptorSerializerOptions = new(JsonSerializerDefaults.Web);
    public const string EngineResourceId = "docker:engine";
    private const string ContainerResourceIdPrefix = "docker:container:";
    private readonly object _gate = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly DockerProviderOptions _options;
    private readonly DockerClient _client;
    private DockerSnapshot _snapshot;

    public DockerContainerResourceProvider(DockerProviderOptions options)
    {
        _options = options;
        Endpoint = options.ResolveEndpoint();
        _client = new DockerClientConfiguration(
            Endpoint,
            defaultTimeout: options.RequestTimeout,
            namedPipeConnectTimeout: options.RequestTimeout)
            .CreateClient();
        var initializedAt = DateTimeOffset.UtcNow;
        _snapshot = DockerSnapshot.Pending(
            Endpoint,
            [
                .. GetEngineResources(ResourceState.Starting, "Connecting", initializedAt),
                .. GetDeclaredContainerResources([], initializedAt)
            ]);
    }

    public string Id => "docker";

    public string DisplayName => "Docker";

    public Uri Endpoint { get; }

    public DockerConnectionStatus ConnectionStatus => GetSnapshot().ConnectionStatus;

    public IReadOnlyList<CloudResource> GetResources() => GetSnapshot().Resources;

    public IReadOnlyList<CloudResource> GetContainers() => GetSnapshot().Resources
        .Where(resource => resource.Kind == "Docker Container")
        .ToArray();

    public IReadOnlyList<LogDescriptor> GetLogs() => GetResources()
        .SelectMany(CreateLogDescriptors)
        .OrderBy(log => log.SourceName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(log => log.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public async Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        if (IsEngineLogId(logId))
        {
            var status = ConnectionStatus;
            return
            [
                new LogEntry(
                    status.LastChecked,
                    status.IsConnected
                        ? $"Connected to Docker Engine at {status.Endpoint}."
                        : $"Docker Engine unavailable at {status.Endpoint}: {status.Error}",
                    status.IsConnected ? "Information" : "Error",
                    "docker-engine")
            ];
        }

        if (!TryGetContainerIdFromLogId(logId, out var containerId))
        {
            return [];
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);

        var parameters = new ContainerLogsParameters
        {
            ShowStdout = true,
            ShowStderr = true,
            Timestamps = true,
            Follow = false,
            Tail = Math.Max(1, maxEntries).ToString(CultureInfo.InvariantCulture),
            Until = before?.AddTicks(-1).UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
        };

        using var stream = await _client.Containers.GetContainerLogsAsync(
            containerId,
            tty: false,
            parameters,
            timeout.Token);
        var (stdout, stderr) = await stream.ReadOutputToEndAsync(timeout.Token);

        return ParseLogOutput(stdout, "stdout", null)
            .Concat(ParseLogOutput(stderr, "stderr", "Error"))
            .OrderBy(entry => entry.Timestamp)
            .TakeLast(maxEntries)
            .ToArray();
    }

    public async IAsyncEnumerable<LogEntry> StreamLogAsync(
        string logId,
        int initialEntries = 50,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!TryGetContainerIdFromLogId(logId, out var containerId))
        {
            yield break;
        }

        if (initialEntries > 0)
        {
            var entries = await ReadLogAsync(logId, initialEntries, cancellationToken: cancellationToken);
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return entry;
            }
        }

        var parameters = new ContainerLogsParameters
        {
            ShowStdout = true,
            ShowStderr = true,
            Timestamps = true,
            Follow = true,
            Tail = "0"
        };

        using var stream = await _client.Containers.GetContainerLogsAsync(
            containerId,
            tty: false,
            parameters,
            cancellationToken);

        await foreach (var entry in ReadContainerLogStreamAsync(stream, cancellationToken))
        {
            yield return entry;
        }
    }

    public async Task SetupEngineAsync(
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var engine = GetEngineResource();

        await registrations.RegisterAsync(
            Id,
            engine.Id,
            NormalizeGroupId(resourceGroupId),
            cancellationToken: cancellationToken);
    }

    public async Task UpdateEngineAsync(
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var engine = GetEngineResource();

        await registrations.AssignToGroupAsync(
            engine.Id,
            NormalizeGroupId(resourceGroupId),
            cancellationToken: cancellationToken);
    }

    public async Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(context.Resource.EffectiveTypeId, "docker.engine", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The Docker provider cannot delete resource '{context.Resource.Id}'.");
        }

        await context.Registrations.RemoveAsync(context.Resource.Id, cancellationToken);
        return ResourceProcedureResult.Completed("Docker Engine registration removed.");
    }

    public bool CanApplyDeclaration(ResourceDeclaration declaration) =>
        string.Equals(declaration.ProviderId, Id, StringComparison.OrdinalIgnoreCase);

    public Task ApplyDeclarationAsync(
        ResourceDeclaration declaration,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var declaredDocker = _options.DeclaredDockerResources.FirstOrDefault(docker =>
            string.Equals(docker.Definition.Id, declaration.ResourceId, StringComparison.OrdinalIgnoreCase));
        if (declaredDocker is not null ||
            string.Equals(declaration.ResourceId, EngineResourceId, StringComparison.OrdinalIgnoreCase))
        {
            if (!declaration.OverwritePersistedState &&
                registrations.GetRegistration(declaration.ResourceId) is not null)
            {
                return Task.CompletedTask;
            }

            return registrations.RegisterAsync(
                Id,
                declaration.ResourceId,
                NormalizeGroupId(declaration.ResourceGroupId),
                declaration.DependsOn,
                cancellationToken);
        }

        var declaredContainer = _options.DeclaredContainers.FirstOrDefault(container =>
            string.Equals(container.Definition.Id, declaration.ResourceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Docker container declaration '{declaration.ResourceId}' was not found.");

        if (!declaration.OverwritePersistedState &&
            registrations.GetRegistration(declaration.ResourceId) is not null)
        {
            return Task.CompletedTask;
        }

        declaredContainer.Definition = declaredContainer.Definition with
        {
            DependsOn = declaration.DependsOn
        };

        return registrations.RegisterAsync(
            Id,
            declaredContainer.Definition.Id,
            NormalizeGroupId(declaration.ResourceGroupId),
            declaration.DependsOn,
            cancellationToken);
    }

    public async Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(context.Resource.EffectiveTypeId, "docker.container", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The Docker provider cannot execute action '{action.Id}' on resource '{context.Resource.Id}'.");
        }

        var containerId = GetContainerId(context.Resource);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);

        switch (action.Kind)
        {
            case ResourceActionKind.Run:
                await _client.Containers.StartContainerAsync(
                    containerId,
                    new ContainerStartParameters(),
                    timeout.Token);
                break;
            case ResourceActionKind.Stop:
                await _client.Containers.StopContainerAsync(
                    containerId,
                    new ContainerStopParameters(),
                    timeout.Token);
                break;
            case ResourceActionKind.Pause:
                await _client.Containers.PauseContainerAsync(containerId, timeout.Token);
                break;
            case ResourceActionKind.Restart:
                await _client.Containers.RestartContainerAsync(
                    containerId,
                    new ContainerRestartParameters(),
                    timeout.Token);
                break;
            case ResourceActionKind.Custom when string.Equals(action.Id, "docker.unpause", StringComparison.OrdinalIgnoreCase):
                await _client.Containers.UnpauseContainerAsync(containerId, timeout.Token);
                break;
            default:
                throw new NotSupportedException(
                    $"Docker does not support action '{action.DisplayName}' for containers.");
        }

        await RefreshAsync(cancellationToken);
        return ResourceProcedureResult.Completed($"{action.DisplayName} requested for {context.Resource.Name}.");
    }

    public bool CanDescribe(CloudResource resource) =>
        string.Equals(resource.EffectiveTypeId, "docker.engine", StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(resource.EffectiveTypeId, "docker.container", StringComparison.OrdinalIgnoreCase) &&
            _options.DeclaredContainers.Any(container =>
                string.Equals(container.Definition.Id, resource.Id, StringComparison.OrdinalIgnoreCase)));

    public Task<ResourceOrchestrationDescriptor> DescribeAsync(
        CloudResource resource,
        ResourceOrchestrationDescriptorContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(resource.EffectiveTypeId, "docker.engine", StringComparison.OrdinalIgnoreCase))
        {
            var engine = new ContainerEngineResourceDefinition(
                resource.Id,
                resource.Name,
                ContainerEngineKind.Docker,
                Endpoint.ToString(),
                IsDefault: string.Equals(resource.Id, EngineResourceId, StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(new ResourceOrchestrationDescriptor(
                resource.Id,
                ContainerEngineResourceTypes.ContainerEngine,
                resource.DependsOn,
                [],
                resource.Endpoints,
                "1.0",
                JsonSerializer.SerializeToElement(engine, DescriptorSerializerOptions)));
        }

        var definition = _options.DeclaredContainers.FirstOrDefault(container =>
                string.Equals(container.Definition.Id, resource.Id, StringComparison.OrdinalIgnoreCase))
            ?.Definition
            ?? throw new InvalidOperationException($"Docker container resource '{resource.Id}' is not configured.");

        var workload = new ResourceWorkloadConfiguration(
            ResourceWorkloadKind.ContainerImage,
            definition.Name,
            Image: definition.Image,
            Replicas: 1,
            Lifetime: definition.Lifetime);

        return Task.FromResult(new ResourceOrchestrationDescriptor(
            resource.Id,
            resource.EffectiveTypeId,
            resource.DependsOn,
            [],
            resource.Endpoints,
            "1.0",
            JsonSerializer.SerializeToElement(workload, DescriptorSerializerOptions)));
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            await QueryDockerAsync(cancellationToken);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _refreshGate.Dispose();
    }

    private DockerSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return _snapshot;
        }
    }

    private CloudResource GetEngineResource() =>
        GetResources().FirstOrDefault(resource =>
            string.Equals(resource.EffectiveTypeId, "docker.engine", StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("The Docker Engine resource is not available.");

    private async Task QueryDockerAsync(CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        DockerSnapshot previousSnapshot;

        lock (_gate)
        {
            previousSnapshot = _snapshot;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.RequestTimeout);

            var containers = await _client.Containers.ListContainersAsync(
                new ContainersListParameters { All = true },
                timeout.Token);

            var declaredContainerNames = GetDeclaredContainerNames();
            var containerResources = containers
                .Where(container => !ContainerMatchesAnyName(container, declaredContainerNames))
                .Select(MapContainer)
                .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var declaredContainerResources = GetDeclaredContainerResources(containers, checkedAt)
                .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var snapshot = new DockerSnapshot(
                [
                    .. GetEngineResources(ResourceState.Running, "Docker Engine API", checkedAt),
                    .. declaredContainerResources,
                    .. containerResources
                ],
                new DockerConnectionStatus(Endpoint, true, null, checkedAt));

            lock (_gate)
            {
                _snapshot = snapshot;
            }
        }
        catch (Exception exception)
        {
            var staleContainers = previousSnapshot.Resources
                .Where(resource => resource.Kind == "Docker Container")
                .Where(resource => !_options.DeclaredContainers.Any(container =>
                    string.Equals(container.Definition.Id, resource.Id, StringComparison.OrdinalIgnoreCase)))
                .Select(resource => resource with { State = ResourceState.Unknown })
                .ToArray();
            var declaredContainerResources = GetDeclaredContainerResources([], checkedAt);

            var snapshot = new DockerSnapshot(
                [
                    .. GetEngineResources(ResourceState.Stopped, "Unavailable", checkedAt),
                    .. declaredContainerResources,
                    .. staleContainers
                ],
                new DockerConnectionStatus(Endpoint, false, GetErrorMessage(exception), checkedAt));

            lock (_gate)
            {
                _snapshot = snapshot;
            }
        }
    }

    private IReadOnlyList<CloudResource> GetEngineResources(
        ResourceState state,
        string version,
        DateTimeOffset lastUpdated)
    {
        var configured = _options.DeclaredDockerResources
            .Select(docker => docker.Definition)
            .Prepend(new DockerResourceDefinition(EngineResourceId, "Local Docker Engine"))
            .DistinctBy(resource => resource.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return configured
            .Select(resource => CreateEngine(resource, state, version, lastUpdated))
            .ToArray();
    }

    private CloudResource CreateEngine(
        DockerResourceDefinition definition,
        ResourceState state,
        string version,
        DateTimeOffset lastUpdated) =>
        new(
            definition.Id,
            definition.Name,
            "Docker Engine",
            DisplayName,
            "local",
            state,
            [new ResourceEndpoint("engine", Endpoint.ToString(), Endpoint.Scheme, false)],
            version,
            lastUpdated,
            [],
            "/resources/docker-engine",
            TypeId: "docker.engine");

    private static IReadOnlyList<LogDescriptor> CreateLogDescriptors(CloudResource resource) =>
        resource.EffectiveTypeId switch
        {
            "docker.engine" =>
            [
                new LogDescriptor(
                    GetEngineLogId(resource.Id),
                    "Engine diagnostics",
                    "Docker",
                    resource.Name,
                    LogSourceKind.Resource,
                    ResourceId: resource.Id,
                    Description: "Docker provider connection and discovery diagnostics.")
            ],
            "docker.container" =>
            [
                new LogDescriptor(
                    $"{resource.Id}:logs",
                    "Container logs",
                    "Docker",
                    resource.Name,
                    LogSourceKind.Resource,
                    ResourceId: resource.Id,
                    SupportsStreaming: true,
                    Description: "Combined stdout and stderr from the Docker container.")
            ],
            _ => []
        };

    private static string GetEngineLogId(string resourceId) => $"{resourceId}:diagnostics";

    private static bool IsEngineLogId(string logId) =>
        logId.EndsWith(":diagnostics", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetContainerIdFromLogId(
        string logId,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? containerId)
    {
        const string logsSuffix = ":logs";
        if (logId.StartsWith(ContainerResourceIdPrefix, StringComparison.OrdinalIgnoreCase) &&
            logId.EndsWith(logsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            containerId = logId[ContainerResourceIdPrefix.Length..^logsSuffix.Length];
            return true;
        }

        containerId = null;
        return false;
    }

    private static async IAsyncEnumerable<LogEntry> ReadContainerLogStreamAsync(
        MultiplexedStream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        try
        {
            while (true)
            {
                var result = await stream.ReadOutputAsync(
                    buffer,
                    0,
                    buffer.Length,
                    cancellationToken);
                if (result.EOF)
                {
                    foreach (var entry in FlushLogChunk(stdout, "stdout", null, final: true))
                    {
                        yield return entry;
                    }

                    foreach (var entry in FlushLogChunk(stderr, "stderr", "Error", final: true))
                    {
                        yield return entry;
                    }

                    yield break;
                }

                var source = result.Target == MultiplexedStream.TargetStream.StandardError
                    ? "stderr"
                    : "stdout";
                var level = result.Target == MultiplexedStream.TargetStream.StandardError
                    ? "Error"
                    : null;
                var pending = result.Target == MultiplexedStream.TargetStream.StandardError
                    ? stderr
                    : stdout;
                var chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);

                foreach (var entry in AppendLogChunk(pending, chunk, source, level))
                {
                    yield return entry;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static IReadOnlyList<LogEntry> ParseLogOutput(
        string? output,
        string source,
        string? level)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var entries = new List<LogEntry>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            entries.Add(ParseLogLine(line, source, level));
        }

        return entries;
    }

    private static IEnumerable<LogEntry> AppendLogChunk(
        StringBuilder pending,
        string chunk,
        string source,
        string? level)
    {
        pending.Append(chunk);

        var text = pending.ToString();
        var lineStart = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            if (index > lineStart)
            {
                yield return ParseLogLine(text[lineStart..index], source, level);
            }
            lineStart = index + 1;
        }

        pending.Clear();
        if (lineStart < text.Length)
        {
            pending.Append(text[lineStart..]);
        }
    }

    private static IEnumerable<LogEntry> FlushLogChunk(
        StringBuilder pending,
        string source,
        string? level,
        bool final)
    {
        if (!final || pending.Length == 0)
        {
            yield break;
        }

        var line = pending.ToString();
        pending.Clear();
        yield return ParseLogLine(line, source, level);
    }

    private static LogEntry ParseLogLine(
        string line,
        string source,
        string? level)
    {
        var trimmed = line.TrimEnd('\r');
        var normalized = StripAnsiEscapeSequences(trimmed);
        var timestamp = DateTimeOffset.UtcNow;
        var message = normalized;
        var separatorIndex = normalized.IndexOf(' ');
        if (separatorIndex > 0 &&
            DateTimeOffset.TryParse(normalized[..separatorIndex], out var parsedTimestamp))
        {
            timestamp = parsedTimestamp;
            message = normalized[(separatorIndex + 1)..];
        }

        return new LogEntry(timestamp, message, level, source);
    }

    private static string StripAnsiEscapeSequences(string value) =>
        AnsiEscapeSequence().Replace(value, string.Empty);

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]")]
    private static partial Regex AnsiEscapeSequence();

    private static CloudResource MapContainer(ContainerListResponse container)
    {
        var id = $"{ContainerResourceIdPrefix}{container.ID}";
        var name = container.Names?
            .Select(value => value.Trim('/'))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? container.ID[..Math.Min(container.ID.Length, 12)];

        return new CloudResource(
            id,
            name,
            "Docker Container",
            "Docker",
            "local",
            MapState(container.State),
            CreateEndpoints(name, container.Ports),
            container.Image,
            new DateTimeOffset(container.Created.ToUniversalTime()),
            [],
            ParentResourceId: EngineResourceId,
            TypeId: "docker.container",
            Actions: CreateContainerActions(container.State));
    }

    private IReadOnlyList<CloudResource> GetDeclaredContainerResources(
        IEnumerable<ContainerListResponse> containers,
        DateTimeOffset lastUpdated) =>
        _options.DeclaredContainers
            .Select(container => MapDeclaredContainer(
                container.Definition,
                FindContainer(containers, GetContainerLookupName(container.Definition.Id)),
                lastUpdated))
            .ToArray();

    private static CloudResource MapDeclaredContainer(
        DockerContainerResourceDefinition definition,
        ContainerListResponse? container,
        DateTimeOffset lastUpdated)
    {
        var lookupName = GetContainerLookupName(definition.Id);
        var endpoints = container is not null
            ? CreateEndpoints(lookupName, container.Ports)
            : definition.Endpoints.Count > 0
                ? definition.Endpoints
                : [new ResourceEndpoint("container", $"container://{lookupName}", "container", false)];

        return new CloudResource(
            definition.Id,
            definition.Name,
            "Docker Container",
            "Docker",
            "local",
            container is null ? ResourceState.Unknown : MapState(container.State),
            endpoints,
            container?.Image ?? definition.Image,
            container is null
                ? lastUpdated
                : new DateTimeOffset(container.Created.ToUniversalTime()),
            NormalizeDependencies(definition.DockerResourceId, definition.DependsOn),
            ParentResourceId: definition.DockerResourceId,
            TypeId: "docker.container",
            Actions: container is null ? [] : CreateContainerActions(container.State),
            HealthChecks: definition.HealthChecks);
    }

    public static string CreateDockerResourceId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var normalized = id.Trim();
        return normalized.StartsWith("docker:", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"docker:{normalized}";
    }

    public static string CreateContainerResourceId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var normalized = id.Trim();
        return normalized.StartsWith(ContainerResourceIdPrefix, StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"{ContainerResourceIdPrefix}{normalized.TrimStart('/')}";
    }

    private IReadOnlySet<string> GetDeclaredContainerNames() =>
        _options.DeclaredContainers
            .Select(container => GetContainerLookupName(container.Definition.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static ContainerListResponse? FindContainer(
        IEnumerable<ContainerListResponse> containers,
        string name) =>
        containers.FirstOrDefault(container => ContainerMatchesName(container, name));

    private static bool ContainerMatchesAnyName(
        ContainerListResponse container,
        IReadOnlySet<string> names) =>
        names.Count > 0 && names.Any(name => ContainerMatchesName(container, name));

    private static bool ContainerMatchesName(
        ContainerListResponse container,
        string name) =>
        string.Equals(container.ID, name, StringComparison.OrdinalIgnoreCase) ||
        (container.Names ?? [])
            .Select(NormalizeContainerName)
            .Any(containerName => string.Equals(containerName, name, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeContainerName(string value) =>
        value.Trim().TrimStart('/');

    private static IReadOnlyList<string> NormalizeDependencies(
        string dockerResourceId,
        IReadOnlyList<string> dependsOn) =>
        dependsOn
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
            .Select(dependency => dependency.Trim())
            .Where(dependency => !dependency.Equals(dockerResourceId, StringComparison.OrdinalIgnoreCase))
            .Prepend(dockerResourceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<ResourceAction> CreateContainerActions(string? state) =>
        state?.ToLowerInvariant() switch
        {
            "running" =>
            [
                ResourceAction.Stop,
                ResourceAction.Pause,
                ResourceAction.Restart
            ],
            "paused" =>
            [
                new ResourceAction("docker.unpause", "Resume"),
                ResourceAction.Stop,
                ResourceAction.Restart
            ],
            "created" or "exited" or "dead" =>
            [
                ResourceAction.Run
            ],
            "restarting" =>
            [
                ResourceAction.Stop
            ],
            _ => []
        };

    private static IReadOnlyList<ResourceEndpoint> CreateEndpoints(
        string containerName,
        IList<Port>? ports)
    {
        if (ports is null || ports.Count == 0)
        {
            return [new("container", $"container://{containerName}", "container", false)];
        }

        return ports
            .Select((port, index) =>
            {
                var protocol = string.IsNullOrWhiteSpace(port.Type) ? "tcp" : port.Type;
                var isExternal = port.PublicPort > 0;
                var address = isExternal
                    ? $"{protocol}://{NormalizeHost(port.IP)}:{port.PublicPort}"
                    : $"{protocol}://{containerName}:{port.PrivatePort}";

                return new ResourceEndpoint(
                    $"port-{index + 1}",
                    address,
                    protocol,
                    isExternal);
            })
            .ToArray();
    }

    private static string NormalizeHost(string? host) =>
        string.IsNullOrWhiteSpace(host) || host == "0.0.0.0" || host == "::"
            ? "localhost"
            : host;

    private static ResourceState MapState(string? state) =>
        state?.ToLowerInvariant() switch
        {
            "running" => ResourceState.Running,
            "created" or "restarting" => ResourceState.Starting,
            "paused" => ResourceState.Paused,
            "exited" or "dead" => ResourceState.Stopped,
            _ => ResourceState.Unknown
        };

    private static string GetContainerId(CloudResource resource)
    {
        if (!resource.Id.StartsWith(ContainerResourceIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Resource '{resource.Id}' is not a Docker container resource.");
        }

        return resource.Id[ContainerResourceIdPrefix.Length..];
    }

    private static string GetContainerLookupName(string resourceId) =>
        resourceId.StartsWith(ContainerResourceIdPrefix, StringComparison.OrdinalIgnoreCase)
            ? resourceId[ContainerResourceIdPrefix.Length..]
            : resourceId;

    private static string? NormalizeGroupId(string? resourceGroupId) =>
        string.IsNullOrWhiteSpace(resourceGroupId) ? null : resourceGroupId;

    private static string GetErrorMessage(Exception exception)
    {
        var error = exception;
        while (error.InnerException is not null)
        {
            error = error.InnerException;
        }

        return error.Message;
    }

    private sealed record DockerSnapshot(
        IReadOnlyList<CloudResource> Resources,
        DockerConnectionStatus ConnectionStatus)
    {
        public static DockerSnapshot Pending(Uri endpoint, IReadOnlyList<CloudResource> resources) =>
            new(
                resources,
                new DockerConnectionStatus(endpoint, false, "Connecting to Docker.", DateTimeOffset.MinValue));
    }
}
