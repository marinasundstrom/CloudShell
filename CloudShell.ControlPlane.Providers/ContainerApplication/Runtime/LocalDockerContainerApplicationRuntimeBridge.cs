using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GraphResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class LocalDockerContainerApplicationRuntimeBridge(
    ILocalContainerApplicationCommandRunner commandRunner,
    IOptions<LocalDockerContainerApplicationRuntimeOptions> options,
    IConfiguration configuration,
    IHostEnvironment? hostEnvironment = null) : ILocalDockerContainerApplicationRuntimeBridge
{
    private const string ReplicaGroupLabel = "cloudshell.replica-group-id";
    private const string RuntimeRevisionLabel = "cloudshell.runtime-revision-id";
    private readonly LocalDockerContainerApplicationRuntimeOptions options = options.Value;
    private readonly string? runtimeNameScope = FirstNonEmpty(
        options.Value.NameScope,
        ResolveRuntimeNameScope(configuration, hostEnvironment));
    private readonly object _statusGate = new();
    private readonly TimeSpan _statusProbeTimeout = TimeSpan.FromMilliseconds(
        configuration.GetValue<int?>("ReplicatedContainerHealth:RuntimeStatusProbeTimeoutMilliseconds") ?? 1_000);
    private readonly TimeSpan _statusCacheDuration = TimeSpan.FromMilliseconds(
        configuration.GetValue<int?>("ReplicatedContainerHealth:RuntimeStatusCacheMilliseconds") ?? 2_000);
    private readonly int _replicaCleanupLimit = Math.Max(
        1,
        configuration.GetValue<int?>("ReplicatedContainerHealth:RuntimeReplicaCleanupLimit") ?? 10);
    private ContainerApplicationRuntimeStatus? _cachedStatus;
    private ContainerApplicationRuntimeStatus? _lastStableStatus;
    private DateTimeOffset _cachedStatusTimestamp;

    public bool CanHandle(GraphResource resource) =>
        TryResolveDefinition(resource, out _);

    public bool TryResolveDefinition(
        GraphResource resource,
        out LocalDockerContainerApplicationRuntimeDefinition definition)
    {
        if (options.TryGetApplication(resource.EffectiveResourceId, out definition!))
        {
            return true;
        }

        if (resource.Type.TypeId != ContainerApplicationResourceTypeProvider.ResourceTypeId ||
            string.IsNullOrWhiteSpace(resource.Attributes.GetString(
                ContainerApplicationResourceTypeProvider.Attributes.ContainerImage)))
        {
            definition = null!;
            return false;
        }

        definition = LocalDockerContainerApplicationRuntimeDefinition.CreateDefault(
            resource.EffectiveResourceId,
            resource.Attributes.GetString(ResourceAttributeId.Create(ResourceAttributeNames.ProjectPath)),
            runtimeNameScope);
        return true;
    }

    public ContainerApplicationRuntimeStatus GetStatus(GraphResource resource)
    {
        if (!TryResolveDefinition(resource, out var definition))
        {
            return ContainerApplicationRuntimeStatus.Unknown;
        }

        var now = DateTimeOffset.UtcNow;
        var statusCacheDuration = definition.StatusCacheDuration ?? _statusCacheDuration;
        lock (_statusGate)
        {
            if (_cachedStatus is not null &&
                now - _cachedStatusTimestamp <= statusCacheDuration)
            {
                return _cachedStatus.Value;
            }
        }

        var probe = ResolveStatus(resource, definition);
        var status = probe.Status;
        lock (_statusGate)
        {
            if (probe.IsTransient && _lastStableStatus is not null)
            {
                status = _lastStableStatus.Value;
            }

            _cachedStatus = status;
            _cachedStatusTimestamp = now;
            if (status != ContainerApplicationRuntimeStatus.Unknown)
            {
                _lastStableStatus = status;
            }
        }

        return status;
    }

    public bool TryGetObservedStatus(
        GraphResource resource,
        out ContainerApplicationRuntimeStatus status)
    {
        status = ContainerApplicationRuntimeStatus.Unknown;
        if (!TryResolveDefinition(resource, out _))
        {
            return false;
        }

        lock (_statusGate)
        {
            if (_cachedStatus is null)
            {
                return false;
            }

            status = _cachedStatus.Value;
            return true;
        }
    }

    private RuntimeStatusProbeResult ResolveStatus(
        GraphResource resource,
        LocalDockerContainerApplicationRuntimeDefinition definition)
    {
        var replicas = ResolveReplicas(resource);
        var running = 0;
        var stopped = 0;
        var statusProbeTimeout = definition.StatusProbeTimeout ?? _statusProbeTimeout;

        for (var replica = 1; replica <= replicas; replica++)
        {
            var result = commandRunner.Run(
                "docker",
                [
                    "container",
                    "inspect",
                    "--format",
                    "{{.State.Status}}",
                    LocalDockerContainerApplicationRuntimeConventions.CreateReplicaContainerName(definition, replica)
                ],
                throwOnError: false,
                timeout: statusProbeTimeout);
            if (result.ExitCode == LocalContainerApplicationCommandResult.TimeoutExitCode)
            {
                return RuntimeStatusProbeResult.Transient();
            }

            if (result.ExitCode == LocalContainerApplicationCommandResult.UnavailableExitCode)
            {
                return RuntimeStatusProbeResult.Unknown();
            }

            if (result.ExitCode != 0)
            {
                stopped++;
                continue;
            }

            switch (result.Output.Trim().ToLowerInvariant())
            {
                case "running":
                    running++;
                    break;
                case "created":
                case "exited":
                case "dead":
                    stopped++;
                    break;
                default:
                    return RuntimeStatusProbeResult.Unknown();
            }
        }

        if (running == replicas)
        {
            return RuntimeStatusProbeResult.Running();
        }

        return stopped == replicas
            ? RuntimeStatusProbeResult.Stopped()
            : RuntimeStatusProbeResult.Unknown();
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        GraphResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        var definition = ResolveDefinition(resource);
        var operationDescription = DescribeLifecycleOperation(operationId);
        try
        {
            switch (operationId.ToString())
            {
                case ResourceActionIds.Start:
                    await StartAsync(definition, resource, cancellationToken);
                    ObserveStatus(ContainerApplicationRuntimeStatus.Running);
                    break;
                case ResourceActionIds.Stop:
                    await RemoveAsync(definition, resource, cancellationToken);
                    ObserveStatus(ContainerApplicationRuntimeStatus.Stopped);
                    break;
                case ResourceActionIds.Restart:
                    await RemoveAsync(definition, resource, cancellationToken);
                    await StartAsync(definition, resource, cancellationToken, cleanExistingReplicas: false);
                    ObserveStatus(ContainerApplicationRuntimeStatus.Running);
                    break;
                default:
                    throw new NotSupportedException(
                        $"The local Docker container application runtime does not map graph operation '{operationId}' to a runtime operation.");
            }

            return [];
        }
        catch (Exception exception)
        {
            return [RuntimeFailed(resource, operationDescription, exception)];
        }
    }

    public string? GetOperationUnavailableReason(
        GraphResource resource,
        ResourceOperationId operationId)
    {
        if (operationId == ContainerApplicationResourceTypeProvider.Operations.Stop ||
            !RequiresIngress(resource) ||
            CreateSessionAffinityPolicyFromResource(resource)?.Mode !=
                ResourceOrchestratorSessionAffinityMode.ClientIp)
        {
            return null;
        }

        return $"Local Docker container app runtime does not support ClientIp session affinity for '{resource.EffectiveResourceId}'. Use Cookie affinity or disable session affinity.";
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        var definition = ResolveDefinition(resource);
        try
        {
            var desiredReplicas = ResolveReplicas(resource);
            var inspectedReplicas = await InspectReplicasAsync(
                definition,
                Math.Max(desiredReplicas, ResolveReplicaCleanupLimit(definition, resource)),
                cancellationToken);
            if (!inspectedReplicas.Any(replica => IsMaterialized(replica.Status)))
            {
                return [];
            }

            var image = ResolveImage(resource);
            await PublishImageAsync(definition, resource, image, cancellationToken);
            await EnsureContainerNetworkAsync(definition, cancellationToken);

            for (var replica = 1; replica <= desiredReplicas; replica++)
            {
                var status = inspectedReplicas.FirstOrDefault(candidate => candidate.Ordinal == replica)?.Status ??
                    RuntimeContainerStatus.Missing;
                if (IsMaterialized(status))
                {
                    await RemoveReplicaAsync(definition, replica, cancellationToken);
                }

                await commandRunner.RunAsync(
                    "docker",
                    CreateRunArguments(definition, resource, image, replica),
                    cancellationToken);
            }

            await StartIngressAsync(
                definition,
                resource,
                cancellationToken,
                createWhenMissing: desiredReplicas > 1);

            foreach (var replica in inspectedReplicas
                         .Where(candidate => candidate.Ordinal > desiredReplicas && IsMaterialized(candidate.Status))
                         .OrderByDescending(candidate => candidate.Ordinal))
            {
                await RemoveReplicaAsync(definition, replica.Ordinal, cancellationToken);
            }

            ObserveStatus(ContainerApplicationRuntimeStatus.Running);
            return [];
        }
        catch (Exception exception)
        {
            return [RuntimeFailed(resource, "applying image changes", exception)];
        }
    }

    private void ObserveStatus(ContainerApplicationRuntimeStatus status)
    {
        lock (_statusGate)
        {
            _cachedStatus = status;
            _cachedStatusTimestamp = DateTimeOffset.UtcNow;
            if (status != ContainerApplicationRuntimeStatus.Unknown)
            {
                _lastStableStatus = status;
            }
        }
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        var definition = ResolveDefinition(resource);
        try
        {
            var desiredReplicas = ResolveReplicas(resource);
            var inspectedReplicas = await InspectReplicasAsync(
                definition,
                Math.Max(desiredReplicas, ResolveReplicaCleanupLimit(definition, resource)),
                cancellationToken);
            if (!inspectedReplicas.Any(replica => IsMaterialized(replica.Status)))
            {
                return [];
            }

            var image = ResolveImage(resource);
            await EnsureContainerNetworkAsync(definition, cancellationToken);
            for (var replica = 1; replica <= desiredReplicas; replica++)
            {
                var status = inspectedReplicas.FirstOrDefault(candidate => candidate.Ordinal == replica)?.Status ??
                    RuntimeContainerStatus.Missing;
                if (!IsMaterialized(status))
                {
                    await commandRunner.RunAsync(
                        "docker",
                        CreateRunArguments(definition, resource, image, replica),
                        cancellationToken);
                }
            }

            await StartIngressAsync(
                definition,
                resource,
                cancellationToken,
                createWhenMissing: desiredReplicas > 1);

            foreach (var replica in inspectedReplicas
                         .Where(candidate => candidate.Ordinal > desiredReplicas && IsMaterialized(candidate.Status))
                         .OrderByDescending(candidate => candidate.Ordinal))
            {
                await RemoveReplicaAsync(definition, replica.Ordinal, cancellationToken);
            }

            ObserveStatus(ContainerApplicationRuntimeStatus.Running);
            return [];
        }
        catch (Exception exception)
        {
            return [RuntimeFailed(resource, "applying replica changes", exception)];
        }
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> PrepareOrchestratorServiceAsync(
        GraphResource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
        CancellationToken cancellationToken = default)
    {
        var definition = ResolveDefinition(resource);
        try
        {
            await PublishImageAsync(definition, resource, ResolveImage(resource), cancellationToken);
            await EnsureContainerNetworkAsync(definition, cancellationToken);
            return [];
        }
        catch (Exception exception)
        {
            return [RuntimeFailed(resource, "preparing orchestrator service runtime", exception)];
        }
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileOrchestratorServiceRoutingAsync(
        GraphResource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
        CancellationToken cancellationToken = default)
    {
        var definition = ResolveDefinition(resource);
        var unsupportedAffinityDiagnostic = GetUnsupportedSessionAffinityDiagnostic(
            resource,
            routingBindings);
        if (unsupportedAffinityDiagnostic is not null)
        {
            return [unsupportedAffinityDiagnostic];
        }

        try
        {
            await StartIngressAsync(
                definition,
                resource,
                cancellationToken,
                createWhenMissing: ResolveReplicas(resource) > 1,
                replicaGroup: replicaGroup,
                routingBindings: routingBindings);
            ObserveStatus(ContainerApplicationRuntimeStatus.Running);
            return [];
        }
        catch (Exception exception)
        {
            return [RuntimeFailed(resource, "reconciling orchestrator service routing", exception)];
        }
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> TearDownOrchestratorServiceRoutingAsync(
        GraphResource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
        CancellationToken cancellationToken = default)
    {
        var definition = ResolveDefinition(resource);
        try
        {
            await RemoveIngressAsync(definition, cancellationToken);
            ObserveStatus(ContainerApplicationRuntimeStatus.Running);
            return [];
        }
        catch (Exception exception)
        {
            return [RuntimeFailed(resource, "tearing down orchestrator service routing", exception)];
        }
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteOrchestratorServiceInstanceAsync(
        GraphResource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorServiceInstance instance,
        ResourceAction action,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        CancellationToken cancellationToken = default)
    {
        var definition = ResolveDefinition(resource);
        try
        {
            switch (action.Kind)
            {
                case ResourceActionKind.Start:
                    await StartReplicaAsync(
                        definition,
                        resource,
                        ResolveImage(resource),
                        instance.ReplicaOrdinal,
                        cancellationToken,
                        replicaGroup: replicaGroup);
                    break;
                case ResourceActionKind.Stop:
                    await RemoveReplicaAsync(
                        definition,
                        instance.ReplicaOrdinal,
                        replicaGroup,
                        cancellationToken);
                    break;
                default:
                    throw new NotSupportedException(
                        $"The local Docker container application runtime does not map graph action '{action.Id}' to an orchestrator service instance operation.");
            }

            ObserveStatus(ContainerApplicationRuntimeStatus.Running);
            return [];
        }
        catch (Exception exception)
        {
            return [RuntimeFailed(
                resource,
                $"executing orchestrator service instance action '{action.Id}'",
                exception)];
        }
    }

    private async Task StartAsync(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        GraphResource resource,
        CancellationToken cancellationToken,
        bool cleanExistingReplicas = true)
    {
        var image = ResolveImage(resource);
        await PublishImageAsync(definition, resource, image, cancellationToken);

        try
        {
            var replicas = ResolveReplicas(resource);
            if (cleanExistingReplicas)
            {
                await RemoveIngressAsync(definition, cancellationToken);
                await RemoveReplicasAsync(definition, ResolveReplicaCleanupLimit(definition, resource), cancellationToken);
            }

            await EnsureContainerNetworkAsync(definition, cancellationToken);
            for (var replica = 1; replica <= replicas; replica++)
            {
                await StartReplicaAsync(
                    definition,
                    resource,
                    image,
                    replica,
                    cancellationToken,
                    replaceExisting: false);
            }

            await StartIngressAsync(
                definition,
                resource,
                cancellationToken,
                reuseExisting: false);
        }
        catch
        {
            await RemoveAsync(definition, resource, cancellationToken);
            throw;
        }
    }

    private async Task StartReplicaAsync(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        GraphResource resource,
        string image,
        int replica,
        CancellationToken cancellationToken,
        bool replaceExisting = true,
        ResourceOrchestratorReplicaGroup? replicaGroup = null)
    {
        if (replaceExisting)
        {
            await RemoveReplicaAsync(definition, replica, cancellationToken);
        }

        await commandRunner.RunAsync(
            "docker",
            CreateRunArguments(definition, resource, image, replica, replicaGroup),
            cancellationToken);
    }

    private async Task PublishImageAsync(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        GraphResource resource,
        string image,
        CancellationToken cancellationToken)
    {
        await BuildJavaProjectAsync(resource, cancellationToken);

        var buildContext = resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerBuildContext);
        if (!string.IsNullOrWhiteSpace(buildContext))
        {
            var arguments = new List<string>
            {
                "build",
                "-t",
                image
            };
            var dockerfile = resource.Attributes.GetString(
                ContainerApplicationResourceTypeProvider.Attributes.ContainerDockerfile);
            if (!string.IsNullOrWhiteSpace(dockerfile))
            {
                arguments.Add("-f");
                arguments.Add(ResolveDockerfilePath(dockerfile, buildContext));
            }

            arguments.Add(ResolvePath(buildContext));
            await RunMaterializationCommandAsync(
                definition,
                "docker",
                arguments,
                cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(definition.ProjectPath))
        {
            return;
        }

        var (repository, tag) = SplitImage(image);
        await RunMaterializationCommandAsync(
            definition,
            "dotnet",
            [
                "publish",
                definition.ResolveProjectPath(hostEnvironment),
                "--os",
                definition.ContainerPublishOperatingSystem,
                "--arch",
                definition.ContainerPublishArchitecture,
                "/t:PublishContainer",
                $"-p:ContainerRepository={repository}",
                $"-p:ContainerImageTag={tag}"
            ],
            cancellationToken);

        string ResolvePath(string path) =>
            ResolveHostPath(path);

        string ResolveDockerfilePath(string dockerfile, string context) =>
            Path.IsPathRooted(dockerfile)
                ? dockerfile
                : Path.Combine(ResolvePath(context), dockerfile);
    }

    private async Task RunMaterializationCommandAsync(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        string? workingDirectory = null)
    {
        var result = await commandRunner.RunAsync(
            fileName,
            arguments,
            cancellationToken,
            throwOnError: false,
            timeout: definition.MaterializationCommandTimeout,
            workingDirectory: workingDirectory);

        if (result.ExitCode == 0)
        {
            return;
        }

        var message = CreateMaterializationCommandFailureMessage(fileName, arguments, result);
        if (result.ExitCode == LocalContainerApplicationCommandResult.TimeoutExitCode)
        {
            throw new TimeoutException(message);
        }

        throw new InvalidOperationException(message);
    }

    private static string CreateMaterializationCommandFailureMessage(
        string fileName,
        IReadOnlyList<string> arguments,
        LocalContainerApplicationCommandResult result)
    {
        var command = string.Join(' ', new[] { fileName }.Concat(arguments));
        var reason = result.ExitCode switch
        {
            LocalContainerApplicationCommandResult.TimeoutExitCode => "timed out",
            LocalContainerApplicationCommandResult.UnavailableExitCode => "is unavailable",
            _ => $"failed with exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}"
        };
        var detail = FirstNonEmpty(result.Error, result.Output);
        return string.IsNullOrWhiteSpace(detail)
            ? $"Command '{command}' {reason} while materializing the local Docker container app runtime."
            : $"Command '{command}' {reason} while materializing the local Docker container app runtime: {detail}";
    }

    private async Task BuildJavaProjectAsync(
        GraphResource resource,
        CancellationToken cancellationToken)
    {
        var projectPath = resource.Attributes.GetString(
            JavaAppResourceTypeProvider.Attributes.ProjectPath);
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return;
        }

        var startInfo = new JavaAppProcessCommandFactory()
            .CreateBuildStartInfo(resource, ResolveHostPath(projectPath));
        if (startInfo is null)
        {
            return;
        }

        await commandRunner.RunAsync(
            startInfo.FileName,
            startInfo.ArgumentList.ToArray(),
            cancellationToken,
            workingDirectory: startInfo.WorkingDirectory);
    }

    private string ResolveHostPath(string path) =>
        Path.IsPathRooted(path) || hostEnvironment is null
            ? path
            : Path.Combine(hostEnvironment.ContentRootPath, path);

    private async Task RemoveAsync(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        GraphResource resource,
        CancellationToken cancellationToken)
    {
        await RemoveIngressAsync(definition, cancellationToken);
        await RemoveReplicasAsync(definition, ResolveReplicaCleanupLimit(definition, resource), cancellationToken);
    }

    private Task EnsureContainerNetworkAsync(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        CancellationToken cancellationToken) =>
        commandRunner.RunAsync(
            "docker",
            ["network", "create", definition.ContainerNetworkName],
            cancellationToken,
            throwOnError: false);

    private Task RemoveIngressAsync(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        CancellationToken cancellationToken) =>
        commandRunner.RunAsync(
            "docker",
            [
                "rm",
                "-f",
                LocalDockerContainerApplicationRuntimeConventions.CreateIngressContainerName(definition)
            ],
            cancellationToken,
            throwOnError: false);

    private async Task RemoveReplicasAsync(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        int replicaCount,
        CancellationToken cancellationToken)
    {
        for (var replica = 1; replica <= replicaCount; replica++)
        {
            await RemoveReplicaAsync(definition, replica, cancellationToken);
        }
    }

    private Task RemoveReplicaAsync(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        int replica,
        CancellationToken cancellationToken) =>
        commandRunner.RunAsync(
            "docker",
            [
                "rm",
                "-f",
                LocalDockerContainerApplicationRuntimeConventions.CreateReplicaContainerName(definition, replica)
            ],
            cancellationToken,
            throwOnError: false);

    private async Task RemoveReplicaAsync(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        int replica,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        CancellationToken cancellationToken)
    {
        if (replicaGroup is null)
        {
            await RemoveReplicaAsync(definition, replica, cancellationToken);
            return;
        }

        var containerName = LocalDockerContainerApplicationRuntimeConventions.CreateReplicaContainerName(definition, replica);
        var currentReplicaGroup = await GetContainerLabelAsync(definition, containerName, ReplicaGroupLabel, cancellationToken);
        if (!string.Equals(currentReplicaGroup, replicaGroup.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await RemoveReplicaAsync(definition, replica, cancellationToken);
    }

    private IReadOnlyList<string> CreateRunArguments(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        GraphResource resource,
        string image,
        int replica,
        ResourceOrchestratorReplicaGroup? replicaGroup = null)
    {
        var replicaResourceId = LocalDockerContainerApplicationRuntimeConventions.CreateReplicaResourceId(definition, replica);
        var replicaContainerName = LocalDockerContainerApplicationRuntimeConventions.CreateReplicaContainerName(definition, replica);
        var replicaName = $"Replica {replica.ToString(CultureInfo.InvariantCulture)}";
        var replicaCount = ResolveReplicas(resource);
        var arguments = new List<string>
        {
            "run",
            "-d",
            "--name",
            replicaContainerName,
            "--rm",
            "--network",
            definition.ContainerNetworkName,
            "--network-alias",
            LocalDockerContainerApplicationRuntimeConventions.CreateReplicaNetworkAlias(definition, replica)
        };
        var replicaGroupId = replicaGroup?.Id ??
            LocalDockerContainerApplicationRuntimeConventions.ResolveReplicaGroupId(resource);
        if (!string.IsNullOrWhiteSpace(replicaGroupId))
        {
            arguments.Add("--label");
            arguments.Add($"{ReplicaGroupLabel}={replicaGroupId}");
        }

        var runtimeRevisionId = replicaGroup?.RuntimeRevisionId ??
            LocalDockerContainerApplicationRuntimeConventions.ResolveRuntimeRevisionId(resource);
        if (!string.IsNullOrWhiteSpace(runtimeRevisionId))
        {
            arguments.Add("--label");
            arguments.Add($"{RuntimeRevisionLabel}={runtimeRevisionId}");
        }

        if (TryResolveHttpEndpoint(resource, [], out var endpoint))
        {
            arguments.Add("-p");
            arguments.Add(
                $"127.0.0.1:{LocalDockerContainerApplicationRuntimeConventions.ResolveReplicaProbePort(definition, configuration, replica, endpoint.Port!.Value).ToString(CultureInfo.InvariantCulture)}:{(endpoint.TargetPort ?? endpoint.Port.Value).ToString(CultureInfo.InvariantCulture)}");
        }

        arguments.Add("-e");
        arguments.Add($"CLOUDSHELL_RESOURCE_ID={replicaResourceId}");
        arguments.Add("-e");
        arguments.Add($"CLOUDSHELL_TELEMETRY_RESOURCE_ID={resource.EffectiveResourceId}");
        arguments.Add("-e");
        arguments.Add($"CLOUDSHELL_REPLICA_ORDINAL={replica.ToString(CultureInfo.InvariantCulture)}");
        arguments.Add("-e");
        arguments.Add($"OTEL_SERVICE_NAME={definition.ReplicaServiceNamePrefix}{replica.ToString(CultureInfo.InvariantCulture)}");
        arguments.Add("-e");
        arguments.Add($"OTEL_RESOURCE_ATTRIBUTES={CreateOtelResourceAttributes(definition, replicaResourceId, replicaContainerName, replicaName, replica, replicaCount, runtimeRevisionId)}");
        AddEnvironment(
            arguments,
            "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
            ResolveRuntimeEndpoint(
                definition.TraceIngestEndpoint,
                configuration["Observability:TraceIngestEndpoint"],
                "api/control-plane/v1/traces/ingest"));
        AddEnvironment(
            arguments,
            "CLOUDSHELL_METRIC_INGEST_ENDPOINT",
            ResolveRuntimeEndpoint(
                definition.MetricIngestEndpoint,
                configuration["Observability:MetricIngestEndpoint"],
                "api/control-plane/v1/metrics/ingest"));
        foreach (var variable in ContainerizedProjectEnvironmentVariables.Read(resource))
        {
            AddEnvironment(arguments, variable.Name, variable.Value);
        }

        arguments.Add(image);

        return arguments;
    }

    private async Task<string?> GetContainerLabelAsync(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        string containerName,
        string labelName,
        CancellationToken cancellationToken)
    {
        var result = await commandRunner.RunAsync(
            "docker",
            [
                "container",
                "inspect",
                "--format",
                $"{{{{ index .Config.Labels \"{labelName}\" }}}}",
                containerName
            ],
            cancellationToken,
            throwOnError: false,
            timeout: definition.StatusProbeTimeout ?? _statusProbeTimeout);
        if (result.ExitCode != 0)
        {
            return null;
        }

        var value = result.Output.Trim();
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, "<no value>", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;
    }

    private async Task StartIngressAsync(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        GraphResource resource,
        CancellationToken cancellationToken,
        bool createWhenMissing = true,
        bool reuseExisting = true,
        ResourceOrchestratorReplicaGroup? replicaGroup = null,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition>? routingBindings = null)
    {
        var serviceRoutingBindings = routingBindings ?? [];
        if (!TryResolveHttpEndpoint(resource, serviceRoutingBindings, out var endpoint))
        {
            return;
        }

        await WriteIngressConfigurationAsync(
            definition,
            resource,
            endpoint,
            replicaGroup,
            serviceRoutingBindings,
            cancellationToken);
        if (reuseExisting)
        {
            var ingressStatus = await InspectContainerAsync(
                definition,
                LocalDockerContainerApplicationRuntimeConventions.CreateIngressContainerName(definition),
                cancellationToken);
            if (ingressStatus == RuntimeContainerStatus.Running)
            {
                return;
            }

            if (IsMaterialized(ingressStatus))
            {
                await commandRunner.RunAsync(
                    "docker",
                    ["start", LocalDockerContainerApplicationRuntimeConventions.CreateIngressContainerName(definition)],
                    cancellationToken);
                return;
            }
        }

        var routedReplicaCount = ResolveRoutedReplicaOrdinals(resource, replicaGroup).Count;
        if (!createWhenMissing && routedReplicaCount <= 1)
        {
            return;
        }

        if (routedReplicaCount <= 1)
        {
            return;
        }

        var hostPort = endpoint.Port!.Value;
        var arguments = new List<string>
        {
            "run",
            "-d",
            "--name",
            LocalDockerContainerApplicationRuntimeConventions.CreateIngressContainerName(definition),
            "--network",
            definition.ContainerNetworkName,
            "-p",
            $"127.0.0.1:{hostPort.ToString(CultureInfo.InvariantCulture)}:{hostPort.ToString(CultureInfo.InvariantCulture)}/tcp",
            "-v",
            $"{Path.GetFullPath(definition.ResolveIngressConfigurationDirectory(hostEnvironment))}:/etc/traefik/dynamic:ro",
            FirstNonEmpty(configuration["ReplicatedContainerHealth:RuntimeIngressImage"], definition.IngressImage)!,
            "--providers.file.directory=/etc/traefik/dynamic",
            "--providers.file.watch=true",
            $"--entrypoints.http.address=:{hostPort.ToString(CultureInfo.InvariantCulture)}"
        };

        await commandRunner.RunAsync(
            "docker",
            arguments,
            cancellationToken);
    }

    private async Task<IReadOnlyList<InspectedReplica>> InspectReplicasAsync(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        int replicaCount,
        CancellationToken cancellationToken)
    {
        var replicas = new InspectedReplica[Math.Max(0, replicaCount)];
        for (var replica = 1; replica <= replicas.Length; replica++)
        {
            replicas[replica - 1] = new InspectedReplica(
                replica,
                await InspectContainerAsync(
                    definition,
                    LocalDockerContainerApplicationRuntimeConventions.CreateReplicaContainerName(definition, replica),
                    cancellationToken));
        }

        return replicas;
    }

    private async Task<RuntimeContainerStatus> InspectContainerAsync(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        string containerName,
        CancellationToken cancellationToken)
    {
        var result = await commandRunner.RunAsync(
            "docker",
            [
                "container",
                "inspect",
                "--format",
                "{{.State.Status}}",
                containerName
            ],
            cancellationToken,
            throwOnError: false,
            timeout: definition.StatusProbeTimeout ?? _statusProbeTimeout);
        if (result.ExitCode != 0)
        {
            return RuntimeContainerStatus.Missing;
        }

        return result.Output.Trim().ToLowerInvariant() switch
        {
            "running" => RuntimeContainerStatus.Running,
            "created" or "exited" or "dead" => RuntimeContainerStatus.Stopped,
            _ => RuntimeContainerStatus.Unknown
        };
    }

    private async Task WriteIngressConfigurationAsync(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        GraphResource resource,
        NetworkingEndpointRequestValue endpoint,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
        CancellationToken cancellationToken)
    {
        var ingressConfigurationDirectory = definition.ResolveIngressConfigurationDirectory(hostEnvironment);
        Directory.CreateDirectory(ingressConfigurationDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(ingressConfigurationDirectory, "dynamic.yml"),
            CreateIngressConfiguration(definition, resource, endpoint, replicaGroup, routingBindings),
            cancellationToken);
    }

    private static string CreateIngressConfiguration(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        GraphResource resource,
        NetworkingEndpointRequestValue endpoint,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings)
    {
        var targetPort = endpoint.TargetPort ?? endpoint.Port!.Value;
        var routedReplicas = ResolveRoutedReplicaOrdinals(resource, replicaGroup);
        var sessionAffinity = ResolveSessionAffinity(resource, endpoint, routingBindings);
        var builder = new StringBuilder();
        builder.AppendLine("http:");
        builder.AppendLine("  routers:");
        builder.AppendLine("    api-http:");
        builder.AppendLine("      entryPoints: [\"http\"]");
        builder.AppendLine("      rule: \"PathPrefix(`/`)\"");
        builder.AppendLine("      service: \"api-http\"");
        builder.AppendLine("  services:");
        builder.AppendLine("    api-http:");
        builder.AppendLine("      loadBalancer:");
        builder.AppendLine("        servers:");
        foreach (var replica in routedReplicas)
        {
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"          - url: \"http://{LocalDockerContainerApplicationRuntimeConventions.CreateReplicaNetworkAlias(definition, replica)}:{targetPort.ToString(CultureInfo.InvariantCulture)}\"");
        }

        if (sessionAffinity?.Mode == ResourceOrchestratorSessionAffinityMode.Cookie)
        {
            builder.AppendLine("        sticky:");
            builder.AppendLine("          cookie:");
            if (!string.IsNullOrWhiteSpace(sessionAffinity.CookieName))
            {
                builder.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"            name: \"{EscapeYamlString(sessionAffinity.CookieName)}\"");
            }

            if (sessionAffinity.DurationSeconds is { } durationSeconds)
            {
                builder.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"            maxAge: {Math.Max(1, durationSeconds).ToString(CultureInfo.InvariantCulture)}");
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<int> ResolveRoutedReplicaOrdinals(
        GraphResource resource,
        ResourceOrchestratorReplicaGroup? replicaGroup)
    {
        if (replicaGroup is not null)
        {
            return replicaGroup.Instances
                .Select(instance => instance.ReplicaOrdinal)
                .Where(ordinal => ordinal > 0)
                .Distinct()
                .Order()
                .ToArray();
        }

        return Enumerable
            .Range(1, ResolveReplicas(resource))
            .ToArray();
    }

    private static ResourceOrchestratorSessionAffinityPolicy? ResolveSessionAffinity(
        GraphResource resource,
        NetworkingEndpointRequestValue endpoint,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings)
    {
        var routingBinding = routingBindings.FirstOrDefault(binding =>
            string.Equals(
                binding.SourceEndpoint.ResourceId,
                resource.EffectiveResourceId,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                binding.SourceEndpoint.EndpointName,
                endpoint.Name,
                StringComparison.OrdinalIgnoreCase));
        if (routingBinding?.SessionAffinity is { } sessionAffinity &&
            sessionAffinity.Mode != ResourceOrchestratorSessionAffinityMode.None)
        {
            return sessionAffinity;
        }

        return CreateSessionAffinityPolicyFromResource(resource);
    }

    private static ResourceDefinitionDiagnostic? GetUnsupportedSessionAffinityDiagnostic(
        GraphResource resource,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings)
    {
        if (!TryResolveHttpEndpoint(resource, routingBindings, out var endpoint))
        {
            return null;
        }

        var sessionAffinity = ResolveSessionAffinity(resource, endpoint, routingBindings);
        return sessionAffinity?.Mode == ResourceOrchestratorSessionAffinityMode.ClientIp
            ? ResourceDefinitionDiagnostic.Error(
                "localDockerContainerApplication.sessionAffinityUnsupported",
                $"Local Docker container app runtime does not support ClientIp session affinity for '{resource.EffectiveResourceId}'. Use Cookie affinity or disable session affinity.",
                resource.EffectiveResourceId)
            : null;
    }

    private static ResourceOrchestratorSessionAffinityPolicy? CreateSessionAffinityPolicyFromResource(
        GraphResource resource)
    {
        var modeValue = resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityMode);
        if (string.IsNullOrWhiteSpace(modeValue) ||
            !Enum.TryParse<ResourceOrchestratorSessionAffinityMode>(
                modeValue,
                ignoreCase: true,
                out var mode) ||
            mode == ResourceOrchestratorSessionAffinityMode.None)
        {
            return null;
        }

        var durationValue = resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityDurationSeconds);
        var durationSeconds = int.TryParse(
            durationValue,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsedDuration) && parsedDuration > 0
                ? parsedDuration
                : (int?)null;
        return mode switch
        {
            ResourceOrchestratorSessionAffinityMode.Cookie =>
                ResourceOrchestratorSessionAffinityPolicy.Cookie(
                    resource.Attributes.GetString(
                        ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityCookieName),
                    durationSeconds),
            ResourceOrchestratorSessionAffinityMode.ClientIp =>
                ResourceOrchestratorSessionAffinityPolicy.ClientIp,
            _ => null
        };
    }

    private static string EscapeYamlString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private void AddEnvironment(
        List<string> arguments,
        string name,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        arguments.Add("-e");
        arguments.Add($"{name}={value}");
    }

    private static bool TryResolveHttpEndpoint(
        GraphResource resource,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
        out NetworkingEndpointRequestValue endpoint)
    {
        var endpoints = resource.Attributes
            .GetObject<NetworkingEndpointRequestValue[]>(
                ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests) ?? [];

        foreach (var routingBinding in routingBindings)
        {
            if (!string.Equals(
                    routingBinding.SourceEndpoint.ResourceId,
                    resource.EffectiveResourceId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            endpoint = endpoints.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Name,
                    routingBinding.SourceEndpoint.EndpointName,
                    StringComparison.OrdinalIgnoreCase) &&
                IsHttpEndpoint(candidate))!;
            if (endpoint is { Port: > 0 })
            {
                return true;
            }
        }

        if (routingBindings.Count > 0)
        {
            endpoint = null!;
            return false;
        }

        endpoint = endpoints.FirstOrDefault(IsHttpEndpoint)!;
        return endpoint is { Port: > 0 };
    }

    private static bool IsHttpEndpoint(NetworkingEndpointRequestValue endpoint) =>
        string.Equals(endpoint.Name, "http", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(endpoint.Protocol, "http", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresIngress(GraphResource resource) =>
        ResolveReplicas(resource) > 1 &&
        TryResolveHttpEndpoint(resource, [], out _);

    private LocalDockerContainerApplicationRuntimeDefinition ResolveDefinition(GraphResource resource) =>
        TryResolveDefinition(resource, out var definition)
            ? definition
            : throw new InvalidOperationException(
                $"No local Docker container application runtime is configured for resource '{resource.EffectiveResourceId}'.");

    private static string ResolveImage(GraphResource resource) =>
        resource.Attributes.GetString(ContainerApplicationResourceTypeProvider.Attributes.ContainerImage)
        ?? throw new InvalidOperationException(
            "The container app image must be set before sample runtime can start it.");

    private static int ResolveReplicas(GraphResource resource) =>
        int.TryParse(
            resource.Attributes.GetString(ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas),
            out var replicas)
            ? Math.Max(1, replicas)
            : 1;

    private static string CreateOtelResourceAttributes(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        string replicaResourceId,
        string replicaContainerName,
        string replicaName,
        int replica,
        int replicaCount,
        string runtimeRevisionId)
    {
        var replicaOrdinal = replica.ToString(CultureInfo.InvariantCulture);
        var totalReplicas = replicaCount.ToString(CultureInfo.InvariantCulture);
        return string.Join(
            ',',
            CreateOtelAttribute("service.instance.id", replicaResourceId),
            CreateOtelAttribute("cloudshell.resource.id", replicaResourceId),
            CreateOtelAttribute("cloudshell.resource.type", "runtime.container"),
            CreateOtelAttribute(TelemetryAttributeNames.ScopeResourceId, definition.ResourceId),
            CreateOtelAttribute(TelemetryAttributeNames.ScopeName, replicaName),
            CreateOtelAttribute(TelemetryAttributeNames.ScopeKind, "runtime"),
            CreateOtelAttribute(TelemetryAttributeNames.RuntimeReplicaOrdinal, replicaOrdinal),
            CreateOtelAttribute(TelemetryAttributeNames.RuntimeReplicaCount, totalReplicas),
            CreateOtelAttribute(TelemetryAttributeNames.RuntimeContainerName, replicaContainerName),
            CreateOtelAttribute(TelemetryAttributeNames.DeploymentRevision, runtimeRevisionId));
    }

    private static string CreateOtelAttribute(
        string name,
        string value) =>
        $"{name}={EscapeOtelAttributeValue(value)}";

    private static string EscapeOtelAttributeValue(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("=", "\\=", StringComparison.Ordinal);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? ResolveDockerReachableEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Host is not ("localhost" or "127.0.0.1" or "::1" or "0.0.0.0" or "::"))
        {
            return endpoint;
        }

        var builder = new UriBuilder(uri)
        {
            Host = "host.docker.internal"
        };
        return builder.Uri.GetLeftPart(UriPartial.Authority);
    }

    private string? ResolveRuntimeEndpoint(
        string? explicitEndpoint,
        string? configuredEndpoint,
        string relativePath)
    {
        var endpoint = FirstNonEmpty(explicitEndpoint, configuredEndpoint);
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            return ResolveDockerReachableEndpoint(endpoint);
        }

        var controlPlaneEndpoint = ResolveCloudShellEndpoint();
        if (string.IsNullOrWhiteSpace(controlPlaneEndpoint))
        {
            return null;
        }

        return $"{ResolveDockerReachableEndpoint(controlPlaneEndpoint)}/{relativePath.TrimStart('/')}";
    }

    private string? ResolveCloudShellEndpoint() =>
        FirstHttpEndpoint(configuration["Observability:Endpoint"]) ??
        FirstHttpEndpoint(configuration["urls"]) ??
        FirstHttpEndpoint(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) ??
        (configuration.GetValue<int?>("PORT") is { } port
            ? $"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}"
            : null);

    private static string? ResolveRuntimeNameScope(
        IConfiguration configuration,
        IHostEnvironment? hostEnvironment)
    {
        var configuredScope = FirstNonEmpty(
            configuration["CloudShell:RuntimeNameScope"],
            configuration["CloudShell:EnvironmentId"],
            configuration["CloudShell:EnvironmentName"]);
        if (!string.IsNullOrWhiteSpace(configuredScope))
        {
            return configuredScope;
        }

        var material = string.Join(
            "|",
            new[]
            {
                hostEnvironment?.ContentRootPath,
                FirstHttpEndpoint(configuration["urls"]),
                FirstHttpEndpoint(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")),
                configuration.GetValue<int?>("PORT")?.ToString(CultureInfo.InvariantCulture)
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(material)
            ? null
            : CreateShortRuntimeScope(material);
    }

    private static string CreateShortRuntimeScope(string material)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return $"rt-{Convert.ToHexString(hash, 0, 5).ToLowerInvariant()}";
    }

    private static string? FirstHttpEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        foreach (var candidate in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                uri.Scheme is "http" or "https")
            {
                return uri.GetLeftPart(UriPartial.Authority);
            }
        }

        return null;
    }

    private int ResolveReplicaCleanupLimit(
        LocalDockerContainerApplicationRuntimeDefinition definition,
        GraphResource resource) =>
        Math.Max(ResolveReplicas(resource), definition.ReplicaCleanupLimit ?? _replicaCleanupLimit);

    private static (string Repository, string Tag) SplitImage(string image)
    {
        var separator = image.LastIndexOf(':');
        if (separator <= 0 || separator == image.Length - 1)
        {
            return (image, "latest");
        }

        return (image[..separator], image[(separator + 1)..]);
    }

    private static string DescribeLifecycleOperation(ResourceOperationId operationId) =>
        operationId.ToString() switch
        {
            ResourceActionIds.Start => "starting the app",
            ResourceActionIds.Stop => "stopping the app",
            ResourceActionIds.Restart => "restarting the app",
            _ => $"executing resource operation '{operationId}'"
        };

    private static ResourceDefinitionDiagnostic RuntimeFailed(
        GraphResource resource,
        string operationDescription,
        Exception exception) =>
        ResourceDefinitionDiagnostic.Error(
            "localDockerContainerApplication.runtimeFailed",
            $"Local Docker container app runtime failed while {operationDescription} for '{resource.EffectiveResourceId}': {exception.Message}",
            resource.EffectiveResourceId);

    private sealed record RuntimeStatusProbeResult(
        ContainerApplicationRuntimeStatus Status,
        bool IsTransient = false)
    {
        public static RuntimeStatusProbeResult Running() =>
            new(ContainerApplicationRuntimeStatus.Running);

        public static RuntimeStatusProbeResult Stopped() =>
            new(ContainerApplicationRuntimeStatus.Stopped);

        public static RuntimeStatusProbeResult Unknown() =>
            new(ContainerApplicationRuntimeStatus.Unknown);

        public static RuntimeStatusProbeResult Transient() =>
            new(ContainerApplicationRuntimeStatus.Unknown, IsTransient: true);
    }

    private sealed record InspectedReplica(
        int Ordinal,
        RuntimeContainerStatus Status);

    private static bool IsMaterialized(RuntimeContainerStatus status) =>
        status is RuntimeContainerStatus.Running or RuntimeContainerStatus.Stopped or RuntimeContainerStatus.Unknown;

    private enum RuntimeContainerStatus
    {
        Missing,
        Running,
        Stopped,
        Unknown
    }
}
