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
    IResourceAutoStartPolicyProvider,
    IResourceOrchestrationDescriptorProvider,
    IDisposable
{
    private static readonly JsonSerializerOptions DescriptorSerializerOptions = new(JsonSerializerDefaults.Web);
    public const string HostResourceType = "docker.host";
    public const string LegacyEngineResourceType = "docker.engine";
    public const string DefaultHostResourceId = "docker:engine";
    private const string ContainerResourceIdPrefix = "docker:container:";
    private readonly object _gate = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly DockerProviderOptions _options;
    private readonly Dictionary<string, DockerClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private DockerSnapshot _snapshot;

    public DockerContainerResourceProvider(DockerProviderOptions options)
    {
        _options = options;
        Endpoint = options.ResolveEndpoint();
        var initializedAt = DateTimeOffset.UtcNow;
        _snapshot = DockerSnapshot.Pending(
            Endpoint,
            [
                .. GetHostResources(ResourceState.Starting, "Connecting", initializedAt),
                .. GetConfiguredHosts().SelectMany(host => GetDeclaredContainerResources(host.Id, [], initializedAt))
            ]);
    }

    public string Id => "docker";

    public string DisplayName => "Docker";

    public Uri Endpoint { get; }

    public ContainerRegistryCredentials? RegistryCredentials => _options.RegistryCredentials;

    public DockerConnectionStatus ConnectionStatus => GetSnapshot().ConnectionStatus;

    public IReadOnlyList<Resource> GetResources() => GetSnapshot().Resources;

    public IReadOnlyList<Resource> GetContainers() => GetSnapshot().Resources
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
        if (IsHostLogId(logId))
        {
            var status = ConnectionStatus;
            return
            [
                new LogEntry(
                    status.LastChecked,
                    status.IsConnected
                        ? $"Connected to Docker host at {status.Endpoint}."
                        : $"Docker host unavailable at {status.Endpoint}: {status.Error}",
                    status.IsConnected ? "Information" : "Error",
                    "docker-host")
            ];
        }

        if (!TryGetContainerIdFromLogId(logId, out var containerId))
        {
            return [];
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        var client = GetClientForContainerResourceId(containerId);

        var parameters = new ContainerLogsParameters
        {
            ShowStdout = true,
            ShowStderr = true,
            Timestamps = true,
            Follow = false,
            Tail = Math.Max(1, maxEntries).ToString(CultureInfo.InvariantCulture),
            Until = before?.AddTicks(-1).UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
        };

        using var stream = await client.Containers.GetContainerLogsAsync(
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

        var client = GetClientForContainerResourceId(containerId);
        using var stream = await client.Containers.GetContainerLogsAsync(
            containerId,
            tty: false,
            parameters,
            cancellationToken);

        await foreach (var entry in ReadContainerLogStreamAsync(stream, cancellationToken))
        {
            yield return entry;
        }
    }

    public Task SetupEngineAsync(
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        IReadOnlyList<ResourceHealthCheck>? healthChecks = null,
        string? registry = null,
        ContainerRegistryCredentials? registryCredentials = null,
        CancellationToken cancellationToken = default) =>
        SetupHostAsync(
            DefaultHostResourceId,
            "Local Docker Host",
            DockerHostDefinition.Local(_options.ResolveEndpoint()),
            resourceGroupId,
            registrations,
            healthChecks,
            registry,
            registryCredentials,
            cancellationToken);

    public async Task SetupHostAsync(
        string id,
        string name,
        DockerHostDefinition host,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        IReadOnlyList<ResourceHealthCheck>? healthChecks = null,
        string? registry = null,
        ContainerRegistryCredentials? registryCredentials = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        var normalizedId = CreateDockerResourceId(id);
        var groupId = NormalizeGroupId(resourceGroupId);
        var definition = AddOrUpdateConfiguredHost(
            normalizedId,
            name,
            host,
            healthChecks,
            registry,
            registryCredentials);
        EnsureUniqueHostRegistration(definition, groupId, registrations);

        await registrations.RegisterAsync(
            Id,
            definition.Id,
            groupId,
            cancellationToken: cancellationToken);
    }

    public Task UpdateEngineAsync(
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        string? registry = null,
        ContainerRegistryCredentials? registryCredentials = null,
        CancellationToken cancellationToken = default) =>
        UpdateHostAsync(
            DefaultHostResourceId,
            resourceGroupId,
            registrations,
            registry,
            registryCredentials,
            cancellationToken);

    public async Task UpdateHostAsync(
        string id,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        string? registry = null,
        ContainerRegistryCredentials? registryCredentials = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedId = CreateDockerResourceId(id);
        var host = GetHostDefinition(normalizedId);
        var groupId = NormalizeGroupId(resourceGroupId);
        var definition = AddOrUpdateConfiguredHost(
            normalizedId,
            GetHostName(normalizedId),
            host,
            null,
            registry,
            registryCredentials);
        EnsureUniqueHostRegistration(definition, groupId, registrations);

        await registrations.AssignToGroupAsync(
            definition.Id,
            groupId,
            cancellationToken: cancellationToken);
    }

    private void UpdateRegistry(
        string? registry,
        ContainerRegistryCredentials? registryCredentials)
    {
        if (!string.IsNullOrWhiteSpace(registry))
        {
            _options.Registry = NormalizeRegistry(registry);
            UpdateSnapshotRegistry(_options.Registry);
        }

        _options.RegistryCredentials = ContainerRegistryCredentials.Normalize(registryCredentials);
    }

    private void UpdateSnapshotRegistry(string registry)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                Resources = _snapshot.Resources
                    .Select(resource => IsHostResource(resource) &&
                        string.Equals(resource.Id, DefaultHostResourceId, StringComparison.OrdinalIgnoreCase)
                            ? resource with { Attributes = WithRegistryAttribute(resource.ResourceAttributes, registry) }
                            : resource)
                    .ToArray()
            };
        }
    }

    private static IReadOnlyDictionary<string, string> WithRegistryAttribute(
        IReadOnlyDictionary<string, string> attributes,
        string registry)
    {
        var updated = new Dictionary<string, string>(attributes, StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.ContainerRegistry] = NormalizeRegistry(registry)
        };
        return updated;
    }

    private static bool IsHostResource(Resource resource) =>
        string.Equals(resource.EffectiveTypeId, HostResourceType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resource.EffectiveTypeId, LegacyEngineResourceType, StringComparison.OrdinalIgnoreCase);

    public async Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        if (!IsHostResource(context.Resource))
        {
            throw new InvalidOperationException(
                $"The Docker provider cannot delete resource '{context.Resource.Id}'.");
        }

        await context.Registrations.RemoveAsync(context.Resource.Id, cancellationToken);
        return ResourceProcedureResult.Completed("Container host registration removed.");
    }

    public bool CanApplyDeclaration(ResourceDeclaration declaration) =>
        string.Equals(declaration.ProviderId, Id, StringComparison.OrdinalIgnoreCase);

    public bool CanEvaluateAutoStartPolicy(ResourceDeclaration declaration) =>
        CanApplyDeclaration(declaration);

    public ResourceAutoStartPolicy GetAutoStartPolicy(ResourceDeclaration declaration) =>
        new(
            StartOnControlPlaneStart: !string.Equals(declaration.ResourceId, DefaultHostResourceId, StringComparison.OrdinalIgnoreCase),
            StartAsDependency: true,
            StartAfterCreate: false);

    public Task ApplyDeclarationAsync(
        ResourceDeclaration declaration,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var declaredDocker = _options.DeclaredDockerResources.FirstOrDefault(docker =>
            string.Equals(docker.Definition.Id, declaration.ResourceId, StringComparison.OrdinalIgnoreCase));
        if (declaredDocker is not null ||
            string.Equals(declaration.ResourceId, DefaultHostResourceId, StringComparison.OrdinalIgnoreCase))
        {
            if (!declaration.OverwritePersistedState &&
                registrations.GetRegistration(declaration.ResourceId) is not null)
            {
                return Task.CompletedTask;
            }

            var definition = declaredDocker?.Definition ?? GetDefaultHostDefinition();
            EnsureUniqueHostRegistration(definition, NormalizeGroupId(declaration.ResourceGroupId), registrations);

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
        var client = GetClientForContainerResource(context.Resource);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);

        switch (action.Kind)
        {
            case ResourceActionKind.Start:
                await client.Containers.StartContainerAsync(
                    containerId,
                    new ContainerStartParameters(),
                    timeout.Token);
                break;
            case ResourceActionKind.Stop:
                await client.Containers.StopContainerAsync(
                    containerId,
                    new ContainerStopParameters(),
                    timeout.Token);
                break;
            case ResourceActionKind.Pause:
                await client.Containers.PauseContainerAsync(containerId, timeout.Token);
                break;
            case ResourceActionKind.Restart:
                await client.Containers.RestartContainerAsync(
                    containerId,
                    new ContainerRestartParameters(),
                    timeout.Token);
                break;
            case ResourceActionKind.Custom when string.Equals(action.Id, "docker.unpause", StringComparison.OrdinalIgnoreCase):
                await client.Containers.UnpauseContainerAsync(containerId, timeout.Token);
                break;
            default:
                throw new NotSupportedException(
                    $"Docker does not support action '{action.DisplayName}' for containers.");
        }

        await RefreshAsync(cancellationToken);
        return ResourceProcedureResult.Completed($"{action.DisplayName} requested for {context.Resource.Name}.");
    }

    public bool CanDescribe(Resource resource) =>
        IsHostResource(resource) ||
        (string.Equals(resource.EffectiveTypeId, "docker.container", StringComparison.OrdinalIgnoreCase) &&
            _options.DeclaredContainers.Any(container =>
                string.Equals(container.Definition.Id, resource.Id, StringComparison.OrdinalIgnoreCase)));

    public Task<ResourceOrchestrationDescriptor> DescribeAsync(
        Resource resource,
        ResourceOrchestrationDescriptorContext context,
        CancellationToken cancellationToken = default)
    {
        if (IsHostResource(resource))
        {
            var hostDefinition = GetHostDefinition(resource.Id);
            var host = new ContainerHostDescriptor(
                resource.Id,
                resource.Name,
                ContainerHostKind.Docker,
                hostDefinition.NormalizedEndpoint,
                IsDefault: string.Equals(resource.Id, DefaultHostResourceId, StringComparison.OrdinalIgnoreCase),
                Registry: GetDockerResourceRegistry(resource.Id),
                RegistryCredentials: GetDockerResourceCredentials(resource.Id),
                CredentialsAvailable: AreHostCredentialsAvailable(hostDefinition.Credentials),
                Capabilities:
                [
                    ContainerHostCapabilityIds.ContainerImage,
                    ContainerHostCapabilityIds.ContainerBuild
                ]);

            return Task.FromResult(new ResourceOrchestrationDescriptor(
                resource.Id,
                ContainerHostResourceTypes.ContainerHost,
                resource.DependsOn,
                [],
                resource.Endpoints,
                "1.0",
                JsonSerializer.SerializeToElement(host, DescriptorSerializerOptions)));
        }

        var definition = _options.DeclaredContainers.FirstOrDefault(container =>
                string.Equals(container.Definition.Id, resource.Id, StringComparison.OrdinalIgnoreCase))
            ?.Definition
            ?? throw new InvalidOperationException($"Docker container resource '{resource.Id}' is not configured.");

        var workload = new ResourceWorkloadConfiguration(
            ResourceWorkloadKind.ContainerImage,
            definition.Name,
            Image: definition.Image,
            Registry: NormalizeRegistry(definition.Registry),
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
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }

        _refreshGate.Dispose();
    }

    private DockerSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return _snapshot;
        }
    }

    private DockerResourceDefinition GetDefaultHostDefinition() =>
        new(
            DefaultHostResourceId,
            "Local Docker Host",
            DockerHostDefinition.Local(_options.ResolveEndpoint()),
            registry: _options.Registry,
            registryCredentials: _options.RegistryCredentials);

    private IReadOnlyList<ConfiguredDockerHost> GetConfiguredHosts() =>
        _options.DeclaredDockerResources
            .Select(docker => docker.Definition)
            .Concat([GetDefaultHostDefinition()])
            .DistinctBy(resource => resource.Id, StringComparer.OrdinalIgnoreCase)
            .Select(definition => new ConfiguredDockerHost(
                definition.Id,
                definition.Name,
                definition.Host ?? DockerHostDefinition.Local(_options.ResolveEndpoint()),
                definition.Registry,
                definition.RegistryCredentials,
                definition.HealthChecks))
            .ToArray();

    private DockerHostDefinition GetHostDefinition(string hostResourceId) =>
        GetConfiguredHosts()
            .FirstOrDefault(host => string.Equals(host.Id, hostResourceId, StringComparison.OrdinalIgnoreCase))
            ?.Host
        ?? DockerHostDefinition.Local(_options.ResolveEndpoint());

    private static bool AreHostCredentialsAvailable(DockerHostCredentials? credentials) =>
        credentials?.Kind switch
        {
            null or DockerHostCredentialKind.None => true,
            DockerHostCredentialKind.UsernamePasswordEnvironmentVariable =>
                !string.IsNullOrWhiteSpace(credentials.Username) &&
                !string.IsNullOrWhiteSpace(credentials.PasswordEnvironmentVariable) &&
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(credentials.PasswordEnvironmentVariable)),
            DockerHostCredentialKind.TlsCertificateFiles =>
                File.Exists(credentials.CertificateAuthorityPath) &&
                File.Exists(credentials.ClientCertificatePath) &&
                File.Exists(credentials.ClientKeyPath),
            _ => false
        };

    private string GetHostName(string hostResourceId) =>
        GetConfiguredHosts()
            .FirstOrDefault(host => string.Equals(host.Id, hostResourceId, StringComparison.OrdinalIgnoreCase))
            ?.Name
        ?? "Docker Host";

    private DockerResourceDefinition AddOrUpdateConfiguredHost(
        string id,
        string name,
        DockerHostDefinition host,
        IReadOnlyList<ResourceHealthCheck>? healthChecks,
        string? registry,
        ContainerRegistryCredentials? registryCredentials)
    {
        var definition = new DockerResourceDefinition(
            id,
            string.IsNullOrWhiteSpace(name) ? "Docker Host" : name.Trim(),
            host,
            healthChecks,
            registry,
            registryCredentials);

        if (string.Equals(definition.Id, DefaultHostResourceId, StringComparison.OrdinalIgnoreCase))
        {
            UpdateRegistry(definition.Registry, definition.RegistryCredentials);
        }

        var declared = _options.DeclaredDockerResources.FirstOrDefault(resource =>
            string.Equals(resource.Definition.Id, definition.Id, StringComparison.OrdinalIgnoreCase));
        if (declared is null)
        {
            _options.DeclaredDockerResources.Add(new DeclaredDockerResource(definition));
        }
        else
        {
            declared.Definition = definition;
        }

        return definition;
    }

    private void EnsureUniqueHostRegistration(
        DockerResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations)
    {
        var identity = GetHostIdentity(definition);
        var duplicate = registrations.GetRegistrations()
            .Where(registration => string.Equals(registration.ProviderId, Id, StringComparison.OrdinalIgnoreCase))
            .Where(registration => string.Equals(
                NormalizeGroupId(registration.ResourceGroupId),
                NormalizeGroupId(resourceGroupId),
                StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(registration =>
                !string.Equals(registration.ResourceId, definition.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(GetHostIdentity(registration.ResourceId), identity, StringComparison.OrdinalIgnoreCase));

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"A container host for '{definition.Host?.NormalizedEndpoint}' is already registered in this resource group as '{duplicate.ResourceId}'.");
        }
    }

    private string GetHostIdentity(DockerResourceDefinition definition) =>
        (definition.Host ?? DockerHostDefinition.Local(_options.ResolveEndpoint())).HostIdentity;

    private string? GetHostIdentity(string hostResourceId) =>
        GetConfiguredHosts()
            .FirstOrDefault(host => string.Equals(host.Id, hostResourceId, StringComparison.OrdinalIgnoreCase))
            ?.Host
            .HostIdentity;

    private DockerClient GetClient(ConfiguredDockerHost host)
    {
        if (_clients.TryGetValue(host.Host.HostIdentity, out var client))
        {
            return client;
        }

        client = new DockerClientConfiguration(
            host.Host.Endpoint,
            defaultTimeout: _options.RequestTimeout,
            namedPipeConnectTimeout: _options.RequestTimeout)
            .CreateClient();
        _clients[host.Host.HostIdentity] = client;
        return client;
    }

    private DockerClient GetClientForContainerResource(Resource resource)
    {
        var host = GetConfiguredHosts()
            .FirstOrDefault(host => string.Equals(host.Id, resource.ParentResourceId, StringComparison.OrdinalIgnoreCase))
            ?? GetConfiguredHosts().First(host => string.Equals(host.Id, DefaultHostResourceId, StringComparison.OrdinalIgnoreCase));
        return GetClient(host);
    }

    private DockerClient GetClientForContainerResourceId(string containerId)
    {
        var resourceId = $"{ContainerResourceIdPrefix}{containerId}";
        var resource = GetResources().FirstOrDefault(resource =>
            string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase));
        if (resource is not null)
        {
            return GetClientForContainerResource(resource);
        }

        var defaultHost = GetConfiguredHosts()
            .First(host => string.Equals(host.Id, DefaultHostResourceId, StringComparison.OrdinalIgnoreCase));
        return GetClient(defaultHost);
    }

    private Resource GetHostResource(IReadOnlyList<ResourceHealthCheck>? healthChecks = null)
    {
        var resource = GetResources().FirstOrDefault(resource =>
            IsHostResource(resource))
            ?? throw new InvalidOperationException("The container host resource is not available.");

        return healthChecks is null
            ? resource
            : resource with { HealthChecks = healthChecks };
    }

    private async Task QueryDockerAsync(CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        DockerSnapshot previousSnapshot;

        lock (_gate)
        {
            previousSnapshot = _snapshot;
        }

        var resources = new List<Resource>();
        DockerConnectionStatus? defaultStatus = null;

        foreach (var host in GetConfiguredHosts())
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_options.RequestTimeout);

                var client = GetClient(host);
                var containers = await client.Containers.ListContainersAsync(
                    new ContainersListParameters { All = true },
                    timeout.Token);

                var declaredContainerNames = GetDeclaredContainerNames(host.Id);
                resources.Add(CreateHost(host, ResourceState.Running, "Docker host API", checkedAt));
                resources.AddRange(GetDeclaredContainerResources(host.Id, containers, checkedAt)
                    .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase));
                resources.AddRange(containers
                    .Where(container => !ContainerMatchesAnyName(container, declaredContainerNames))
                    .Select(container => MapContainer(container, host.Id))
                    .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase));

                var status = new DockerConnectionStatus(host.Host.Endpoint, true, null, checkedAt);
                if (string.Equals(host.Id, DefaultHostResourceId, StringComparison.OrdinalIgnoreCase))
                {
                    defaultStatus = status;
                }
            }
            catch (Exception exception)
            {
                resources.Add(CreateHost(host, ResourceState.Stopped, "Unavailable", checkedAt));
                resources.AddRange(GetDeclaredContainerResources(host.Id, [], checkedAt));
                resources.AddRange(previousSnapshot.Resources
                    .Where(resource => resource.Kind == "Docker Container")
                    .Where(resource => string.Equals(resource.ParentResourceId, host.Id, StringComparison.OrdinalIgnoreCase))
                    .Where(resource => !_options.DeclaredContainers.Any(container =>
                        string.Equals(container.Definition.Id, resource.Id, StringComparison.OrdinalIgnoreCase)))
                    .Select(resource => resource with { State = ResourceState.Unknown }));

                var status = new DockerConnectionStatus(host.Host.Endpoint, false, GetErrorMessage(exception), checkedAt);
                if (string.Equals(host.Id, DefaultHostResourceId, StringComparison.OrdinalIgnoreCase))
                {
                    defaultStatus = status;
                }
            }
        }

        var snapshot = new DockerSnapshot(
            resources
                .GroupBy(resource => resource.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray(),
            defaultStatus ?? new DockerConnectionStatus(Endpoint, false, "Docker host unavailable.", checkedAt));

        lock (_gate)
        {
            _snapshot = snapshot;
        }
    }

    private IReadOnlyList<Resource> GetHostResources(
        ResourceState state,
        string version,
        DateTimeOffset lastUpdated)
    {
        return GetConfiguredHosts()
            .Select(resource => CreateHost(resource, state, version, lastUpdated))
            .ToArray();
    }

    private Resource CreateHost(
        ConfiguredDockerHost configured,
        ResourceState state,
        string version,
        DateTimeOffset lastUpdated) =>
        new(
            configured.Id,
            configured.Name,
            "Container Host",
            DisplayName,
            configured.Host.Kind.ToString().ToLowerInvariant(),
            state,
            [ResourceEndpoint.FromAddress("host", configured.Host.NormalizedEndpoint, configured.Host.Endpoint.Scheme, ResourceExposureScope.Private)],
            version,
            lastUpdated,
            [],
            "/resources/container-hosts",
            TypeId: HostResourceType,
            HealthChecks: configured.HealthChecks,
            ResourceClass: ResourceClass.Infrastructure,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.InfrastructureKind] = ContainerHostKind.Docker.ToString(),
                [ResourceAttributeNames.ContainerRegistry] = NormalizeRegistry(configured.Registry),
                ["docker.host.kind"] = configured.Host.Kind.ToString().ToLowerInvariant(),
                ["docker.host.endpoint"] = configured.Host.NormalizedEndpoint
            },
            Capabilities: [new(ResourceCapabilityIds.ContainerHost)]);

    private static IReadOnlyList<LogDescriptor> CreateLogDescriptors(Resource resource) =>
        resource.EffectiveTypeId switch
        {
            HostResourceType or LegacyEngineResourceType =>
            [
                new LogDescriptor(
                    GetHostLogId(resource.Id),
                    "Host diagnostics",
                    "Docker",
                    resource.Name,
                    LogSourceKind.Resource,
                    ResourceId: resource.Id,
                    Description: "Docker host connection and discovery diagnostics.")
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

    private static string GetHostLogId(string resourceId) => $"{resourceId}:diagnostics";

    private static bool IsHostLogId(string logId) =>
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
                var severity = result.Target == MultiplexedStream.TargetStream.StandardError
                    ? "Error"
                    : null;
                var pending = result.Target == MultiplexedStream.TargetStream.StandardError
                    ? stderr
                    : stdout;
                var chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);

                foreach (var entry in AppendLogChunk(pending, chunk, source, severity))
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
        string? severity)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var entries = new List<LogEntry>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            entries.Add(ParseLogLine(line, source, severity));
        }

        return entries;
    }

    private static IEnumerable<LogEntry> AppendLogChunk(
        StringBuilder pending,
        string chunk,
        string source,
        string? severity)
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
                yield return ParseLogLine(text[lineStart..index], source, severity);
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
        string? severity,
        bool final)
    {
        if (!final || pending.Length == 0)
        {
            yield break;
        }

        var line = pending.ToString();
        pending.Clear();
        yield return ParseLogLine(line, source, severity);
    }

    private static LogEntry ParseLogLine(
        string line,
        string source,
        string? severity)
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

        return new LogEntry(timestamp, message, severity, source);
    }

    private static string StripAnsiEscapeSequences(string value) =>
        AnsiEscapeSequence().Replace(value, string.Empty);

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]")]
    private static partial Regex AnsiEscapeSequence();

    private static Resource MapContainer(ContainerListResponse container, string hostResourceId)
    {
        var id = $"{ContainerResourceIdPrefix}{container.ID}";
        var name = container.Names?
            .Select(value => value.Trim('/'))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? container.ID[..Math.Min(container.ID.Length, 12)];
        var endpoints = CreateEndpoints(name, container.Ports);

        return new Resource(
            id,
            name,
            "Docker Container",
            "Docker",
            "local",
            MapState(container.State),
            endpoints,
            container.Image,
            new DateTimeOffset(container.Created.ToUniversalTime()),
            [],
            ParentResourceId: hostResourceId,
            TypeId: "docker.container",
            Actions: CreateContainerActions(container.State),
            ResourceClass: ResourceClass.Container,
            Attributes: CreateContainerAttributes(container.Image, DockerProviderOptions.DefaultRegistry, endpoints.Count));
    }

    private IReadOnlyList<Resource> GetDeclaredContainerResources(
        string hostResourceId,
        IEnumerable<ContainerListResponse> containers,
        DateTimeOffset lastUpdated) =>
        _options.DeclaredContainers
            .Where(container => string.Equals(container.Definition.DockerResourceId, hostResourceId, StringComparison.OrdinalIgnoreCase))
            .Select(container => MapDeclaredContainer(
                container.Definition,
                FindContainer(containers, GetContainerLookupName(container.Definition.Id)),
                lastUpdated))
            .ToArray();

    private static Resource MapDeclaredContainer(
        DockerContainerResourceDefinition definition,
        ContainerListResponse? container,
        DateTimeOffset lastUpdated)
    {
        var lookupName = GetContainerLookupName(definition.Id);
        var endpoints = container is not null
            ? CreateEndpoints(lookupName, container.Ports)
            : definition.Endpoints.Count > 0
                ? definition.Endpoints
                : [ResourceEndpoint.Logical("container", $"container://{lookupName}", "container")];

        return new Resource(
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
            HealthChecks: definition.HealthChecks,
            ResourceClass: ResourceClass.Container,
            Attributes: CreateContainerAttributes(
                container?.Image ?? definition.Image,
                definition.Registry,
                endpoints.Count));
    }

    private static IReadOnlyDictionary<string, string> CreateContainerAttributes(
        string image,
        string registry,
        int endpointCount) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.WorkloadKind] = ResourceWorkloadKind.ContainerImage.ToString(),
            [ResourceAttributeNames.ContainerImage] = image,
            [ResourceAttributeNames.ContainerRegistry] = NormalizeRegistry(registry),
            [ResourceAttributeNames.ContainerReplicas] = "1",
            [ResourceAttributeNames.EndpointCount] = endpointCount.ToString(CultureInfo.InvariantCulture)
        };

    private string GetDockerResourceRegistry(string dockerResourceId) =>
        _options.DeclaredDockerResources
            .Select(resource => resource.Definition)
            .FirstOrDefault(resource =>
                string.Equals(resource.Id, dockerResourceId, StringComparison.OrdinalIgnoreCase))
            ?.Registry
        ?? (string.Equals(dockerResourceId, DefaultHostResourceId, StringComparison.OrdinalIgnoreCase)
            ? _options.Registry
            : DockerProviderOptions.DefaultRegistry);

    private ContainerRegistryCredentials? GetDockerResourceCredentials(string dockerResourceId) =>
        _options.DeclaredDockerResources
            .Select(resource => resource.Definition)
            .FirstOrDefault(resource =>
                string.Equals(resource.Id, dockerResourceId, StringComparison.OrdinalIgnoreCase))
            ?.RegistryCredentials
        ?? (string.Equals(dockerResourceId, DefaultHostResourceId, StringComparison.OrdinalIgnoreCase)
            ? _options.RegistryCredentials
            : null);

    private static string NormalizeRegistry(string? registry) =>
        string.IsNullOrWhiteSpace(registry)
            ? DockerProviderOptions.DefaultRegistry
            : registry.Trim();

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

    private IReadOnlySet<string> GetDeclaredContainerNames(string hostResourceId) =>
        _options.DeclaredContainers
            .Where(container => string.Equals(container.Definition.DockerResourceId, hostResourceId, StringComparison.OrdinalIgnoreCase))
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
                ResourceAction.Start
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
            return [ResourceEndpoint.Logical("container", $"container://{containerName}", "container")];
        }

        return ports
            .Select((port, index) =>
            {
                var protocol = string.IsNullOrWhiteSpace(port.Type) ? "tcp" : port.Type;
                var isExternal = port.PublicPort > 0;
                var address = isExternal
                    ? $"{protocol}://{NormalizeHost(port.IP)}:{port.PublicPort}"
                    : $"{protocol}://{containerName}:{port.PrivatePort}";

                return ResourceEndpoint.FromAddress(
                    $"port-{index + 1}",
                    address,
                    protocol,
                    isExternal ? ResourceExposureScope.Public : ResourceExposureScope.Private);
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

    private static string GetContainerId(Resource resource)
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
        IReadOnlyList<Resource> Resources,
        DockerConnectionStatus ConnectionStatus)
    {
        public static DockerSnapshot Pending(Uri endpoint, IReadOnlyList<Resource> resources) =>
            new(
                resources,
                new DockerConnectionStatus(endpoint, false, "Connecting to Docker.", DateTimeOffset.MinValue));
    }

    private sealed record ConfiguredDockerHost(
        string Id,
        string Name,
        DockerHostDefinition Host,
        string Registry,
        ContainerRegistryCredentials? RegistryCredentials,
        IReadOnlyList<ResourceHealthCheck> HealthChecks);
}
