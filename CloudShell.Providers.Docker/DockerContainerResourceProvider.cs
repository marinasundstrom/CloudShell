using CloudShell.Abstractions.ResourceManager;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace CloudShell.Providers.Docker;

public sealed class DockerContainerResourceProvider : IResourceProvider, IResourceProcedureProvider, IDisposable
{
    private const string EngineResourceId = "docker:engine";
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
        _snapshot = DockerSnapshot.Pending(Endpoint, CreateEngine(ResourceState.Starting, "Connecting", DateTimeOffset.UtcNow));
    }

    public string Id => "docker";

    public string DisplayName => "Docker";

    public Uri Endpoint { get; }

    public DockerConnectionStatus ConnectionStatus => GetSnapshot().ConnectionStatus;

    public IReadOnlyList<CloudResource> GetResources() => GetSnapshot().Resources;

    public IReadOnlyList<CloudResource> GetContainers() => GetSnapshot().Resources
        .Where(resource => resource.Kind == "Docker Container")
        .ToArray();

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
            cancellationToken);
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
            cancellationToken);
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

            var containerResources = containers
                .Select(MapContainer)
                .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var snapshot = new DockerSnapshot(
                [CreateEngine(ResourceState.Running, "Docker Engine API", checkedAt), .. containerResources],
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
                .Select(resource => resource with { State = ResourceState.Unknown })
                .ToArray();

            var snapshot = new DockerSnapshot(
                [CreateEngine(ResourceState.Stopped, "Unavailable", checkedAt), .. staleContainers],
                new DockerConnectionStatus(Endpoint, false, GetErrorMessage(exception), checkedAt));

            lock (_gate)
            {
                _snapshot = snapshot;
            }
        }
    }

    private CloudResource CreateEngine(
        ResourceState state,
        string version,
        DateTimeOffset lastUpdated) =>
        new(
            EngineResourceId,
            "Local Docker Engine",
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

    private static CloudResource MapContainer(ContainerListResponse container)
    {
        var id = $"docker:container:{container.ID}";
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
        const string prefix = "docker:container:";
        if (!resource.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Resource '{resource.Id}' is not a Docker container resource.");
        }

        return resource.Id[prefix.Length..];
    }

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
        public static DockerSnapshot Pending(Uri endpoint, CloudResource engine) =>
            new(
                [engine],
                new DockerConnectionStatus(endpoint, false, "Connecting to Docker.", DateTimeOffset.MinValue));
    }
}
