using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GraphResource = CloudShell.ResourceModel.Resource;

internal sealed class ReplicatedContainerHealthContainerAppRuntimeBridge(
    ILocalContainerApplicationCommandRunner commandRunner,
    IConfiguration configuration,
    IHostEnvironment? hostEnvironment = null,
    string? traceIngestEndpoint = null,
    string? metricIngestEndpoint = null) : IReplicatedContainerHealthContainerAppRuntimeBridge
{
    private const string DefaultProjectPath = "samples/ReplicatedContainerHealth/Api/CloudShell.ReplicatedContainerHealth.Api.csproj";
    private const string DefaultContainerNetworkName = "cloudshell";
    private const string DefaultIngressImage = "traefik:v3.0";
    private const string ReplicaGroupLabel = "cloudshell.replica-group-id";
    private const string RuntimeRevisionLabel = "cloudshell.runtime-revision-id";
    private readonly string _projectPath = hostEnvironment is null
        ? DefaultProjectPath
        : Path.Combine(hostEnvironment.ContentRootPath, "Api", "CloudShell.ReplicatedContainerHealth.Api.csproj");
    private readonly string _ingressConfigurationDirectory = hostEnvironment is null
        ? Path.Combine("samples", "ReplicatedContainerHealth", "Data", "runtime-ingress")
        : Path.Combine(hostEnvironment.ContentRootPath, "Data", "runtime-ingress");
    private readonly object _statusGate = new();
    private readonly TimeSpan _statusProbeTimeout = TimeSpan.FromMilliseconds(
        configuration.GetValue<int?>("ReplicatedContainerHealth:RuntimeStatusProbeTimeoutMilliseconds") ?? 1_000);
    private readonly TimeSpan _statusCacheDuration = TimeSpan.FromMilliseconds(
        configuration.GetValue<int?>("ReplicatedContainerHealth:RuntimeStatusCacheMilliseconds") ?? 2_000);
    private readonly int _replicaCleanupLimit = Math.Max(
        1,
        configuration.GetValue<int?>("ReplicatedContainerHealth:RuntimeReplicaCleanupLimit") ?? 10);
    private readonly string? _traceIngestEndpoint =
        FirstNonEmpty(traceIngestEndpoint, configuration["Observability:TraceIngestEndpoint"]);
    private readonly string? _metricIngestEndpoint =
        FirstNonEmpty(metricIngestEndpoint, configuration["Observability:MetricIngestEndpoint"]);
    private ContainerApplicationRuntimeStatus? _cachedStatus;
    private ContainerApplicationRuntimeStatus? _lastStableStatus;
    private DateTimeOffset _cachedStatusTimestamp;

    public ContainerApplicationRuntimeStatus GetStatus(GraphResource resource)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_statusGate)
        {
            if (_cachedStatus is not null &&
                now - _cachedStatusTimestamp <= _statusCacheDuration)
            {
                return _cachedStatus.Value;
            }
        }

        var probe = ResolveStatus(resource);
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

    private RuntimeStatusProbeResult ResolveStatus(GraphResource resource)
    {
        var replicas = ResolveReplicas(resource);
        var running = 0;
        var stopped = 0;

        for (var replica = 1; replica <= replicas; replica++)
        {
            var result = commandRunner.Run(
                "docker",
                [
                    "container",
                    "inspect",
                    "--format",
                    "{{.State.Status}}",
                    ReplicatedContainerHealthRuntimeConventions.CreateReplicaContainerName(replica)
                ],
                throwOnError: false,
                timeout: _statusProbeTimeout);
            if (result.ExitCode == LocalContainerApplicationCommandResult.TimeoutExitCode)
            {
                return RuntimeStatusProbeResult.Transient();
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
        try
        {
            switch (operationId.ToString())
            {
                case ResourceActionIds.Start:
                    await StartAsync(resource, cancellationToken);
                    ClearStatusCache();
                    break;
                case ResourceActionIds.Stop:
                    await RemoveAsync(resource, cancellationToken);
                    ClearStatusCache();
                    break;
                case ResourceActionIds.Restart:
                    await RemoveAsync(resource, cancellationToken);
                    await StartAsync(resource, cancellationToken, cleanExistingReplicas: false);
                    ClearStatusCache();
                    break;
                default:
                    throw new NotSupportedException(
                        $"The ReplicatedContainerHealth sample does not map graph operation '{operationId}' to the sample container runtime.");
            }

            return [];
        }
        catch (Exception exception)
        {
            return [RuntimeFailed(resource, exception)];
        }
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var desiredReplicas = ResolveReplicas(resource);
            var inspectedReplicas = await InspectReplicasAsync(
                Math.Max(desiredReplicas, _replicaCleanupLimit),
                cancellationToken);
            if (!inspectedReplicas.Any(replica => IsMaterialized(replica.Status)))
            {
                return [];
            }

            var image = ResolveImage(resource);
            await PublishImageAsync(image, cancellationToken);
            await EnsureContainerNetworkAsync(cancellationToken);

            for (var replica = 1; replica <= desiredReplicas; replica++)
            {
                var status = inspectedReplicas.FirstOrDefault(candidate => candidate.Ordinal == replica)?.Status ??
                    RuntimeContainerStatus.Missing;
                if (IsMaterialized(status))
                {
                    await RemoveReplicaAsync(replica, cancellationToken);
                }

                await commandRunner.RunAsync(
                    "docker",
                    CreateRunArguments(resource, image, replica),
                    cancellationToken);
            }

            await StartIngressAsync(
                resource,
                cancellationToken,
                createWhenMissing: desiredReplicas > 1);

            foreach (var replica in inspectedReplicas
                         .Where(candidate => candidate.Ordinal > desiredReplicas && IsMaterialized(candidate.Status))
                         .OrderByDescending(candidate => candidate.Ordinal))
            {
                await RemoveReplicaAsync(replica.Ordinal, cancellationToken);
            }

            ClearStatusCache();
            return [];
        }
        catch (Exception exception)
        {
            return [RuntimeFailed(resource, exception)];
        }
    }

    private void ClearStatusCache()
    {
        lock (_statusGate)
        {
            _cachedStatus = null;
            _cachedStatusTimestamp = default;
        }
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var desiredReplicas = ResolveReplicas(resource);
            var inspectedReplicas = await InspectReplicasAsync(
                Math.Max(desiredReplicas, _replicaCleanupLimit),
                cancellationToken);
            if (!inspectedReplicas.Any(replica => IsMaterialized(replica.Status)))
            {
                return [];
            }

            var image = ResolveImage(resource);
            await EnsureContainerNetworkAsync(cancellationToken);
            for (var replica = 1; replica <= desiredReplicas; replica++)
            {
                var status = inspectedReplicas.FirstOrDefault(candidate => candidate.Ordinal == replica)?.Status ??
                    RuntimeContainerStatus.Missing;
                if (!IsMaterialized(status))
                {
                    await commandRunner.RunAsync(
                        "docker",
                        CreateRunArguments(resource, image, replica),
                        cancellationToken);
                }
            }

            await StartIngressAsync(
                resource,
                cancellationToken,
                createWhenMissing: desiredReplicas > 1);

            foreach (var replica in inspectedReplicas
                         .Where(candidate => candidate.Ordinal > desiredReplicas && IsMaterialized(candidate.Status))
                         .OrderByDescending(candidate => candidate.Ordinal))
            {
                await RemoveReplicaAsync(replica.Ordinal, cancellationToken);
            }

            ClearStatusCache();
            return [];
        }
        catch (Exception exception)
        {
            return [RuntimeFailed(resource, exception)];
        }
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> PrepareOrchestratorServiceAsync(
        GraphResource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await PublishImageAsync(ResolveImage(resource), cancellationToken);
            await EnsureContainerNetworkAsync(cancellationToken);
            return [];
        }
        catch (Exception exception)
        {
            return [RuntimeFailed(resource, exception)];
        }
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileOrchestratorServiceRoutingAsync(
        GraphResource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await StartIngressAsync(
                resource,
                cancellationToken,
                createWhenMissing: ResolveReplicas(resource) > 1,
                routingBindings: routingBindings);
            ClearStatusCache();
            return [];
        }
        catch (Exception exception)
        {
            return [RuntimeFailed(resource, exception)];
        }
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> TearDownOrchestratorServiceRoutingAsync(
        GraphResource resource,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await RemoveIngressAsync(cancellationToken);
            ClearStatusCache();
            return [];
        }
        catch (Exception exception)
        {
            return [RuntimeFailed(resource, exception)];
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
        try
        {
            switch (action.Kind)
            {
                case ResourceActionKind.Start:
                    await StartReplicaAsync(
                        resource,
                        ResolveImage(resource),
                        instance.ReplicaOrdinal,
                        cancellationToken,
                        replicaGroup: replicaGroup);
                    break;
                case ResourceActionKind.Stop:
                    await RemoveReplicaAsync(
                        instance.ReplicaOrdinal,
                        replicaGroup,
                        cancellationToken);
                    break;
                default:
                    throw new NotSupportedException(
                        $"The ReplicatedContainerHealth sample does not map graph action '{action.Id}' to an orchestrator service instance operation.");
            }

            ClearStatusCache();
            return [];
        }
        catch (Exception exception)
        {
            return [RuntimeFailed(resource, exception)];
        }
    }

    private async Task StartAsync(
        GraphResource resource,
        CancellationToken cancellationToken,
        bool cleanExistingReplicas = true)
    {
        var image = ResolveImage(resource);
        await PublishImageAsync(image, cancellationToken);

        try
        {
            var replicas = ResolveReplicas(resource);
            if (cleanExistingReplicas)
            {
                await RemoveIngressAsync(cancellationToken);
                await RemoveReplicasAsync(ResolveReplicaCleanupLimit(resource), cancellationToken);
            }

            await EnsureContainerNetworkAsync(cancellationToken);
            for (var replica = 1; replica <= replicas; replica++)
            {
                await StartReplicaAsync(
                    resource,
                    image,
                    replica,
                    cancellationToken,
                    replaceExisting: false);
            }

            await StartIngressAsync(
                resource,
                cancellationToken,
                reuseExisting: false);
        }
        catch
        {
            await RemoveAsync(resource, cancellationToken);
            throw;
        }
    }

    private async Task StartReplicaAsync(
        GraphResource resource,
        string image,
        int replica,
        CancellationToken cancellationToken,
        bool replaceExisting = true,
        ResourceOrchestratorReplicaGroup? replicaGroup = null)
    {
        if (replaceExisting)
        {
            await RemoveReplicaAsync(replica, cancellationToken);
        }

        await commandRunner.RunAsync(
            "docker",
            CreateRunArguments(resource, image, replica, replicaGroup),
            cancellationToken);
    }

    private async Task PublishImageAsync(
        string image,
        CancellationToken cancellationToken)
    {
        var (repository, tag) = SplitImage(image);
        await commandRunner.RunAsync(
            "dotnet",
            [
                "publish",
                _projectPath,
                "--os",
                "linux",
                "--arch",
                "x64",
                "/t:PublishContainer",
                $"-p:ContainerRepository={repository}",
                $"-p:ContainerImageTag={tag}"
            ],
            cancellationToken);
    }

    private async Task RemoveAsync(
        GraphResource resource,
        CancellationToken cancellationToken)
    {
        await RemoveIngressAsync(cancellationToken);
        await RemoveReplicasAsync(ResolveReplicaCleanupLimit(resource), cancellationToken);
    }

    private Task EnsureContainerNetworkAsync(CancellationToken cancellationToken) =>
        commandRunner.RunAsync(
            "docker",
            ["network", "create", DefaultContainerNetworkName],
            cancellationToken,
            throwOnError: false);

    private Task RemoveIngressAsync(CancellationToken cancellationToken) =>
        commandRunner.RunAsync(
            "docker",
            [
                "rm",
                "-f",
                ReplicatedContainerHealthRuntimeConventions.CreateIngressContainerName()
            ],
            cancellationToken,
            throwOnError: false);

    private async Task RemoveReplicasAsync(
        int replicaCount,
        CancellationToken cancellationToken)
    {
        for (var replica = 1; replica <= replicaCount; replica++)
        {
            await RemoveReplicaAsync(replica, cancellationToken);
        }
    }

    private Task RemoveReplicaAsync(
        int replica,
        CancellationToken cancellationToken) =>
        commandRunner.RunAsync(
            "docker",
            [
                "rm",
                "-f",
                ReplicatedContainerHealthRuntimeConventions.CreateReplicaContainerName(replica)
            ],
            cancellationToken,
            throwOnError: false);

    private async Task RemoveReplicaAsync(
        int replica,
        ResourceOrchestratorReplicaGroup? replicaGroup,
        CancellationToken cancellationToken)
    {
        if (replicaGroup is null)
        {
            await RemoveReplicaAsync(replica, cancellationToken);
            return;
        }

        var containerName = ReplicatedContainerHealthRuntimeConventions.CreateReplicaContainerName(replica);
        var currentReplicaGroup = await GetContainerLabelAsync(containerName, ReplicaGroupLabel, cancellationToken);
        if (!string.Equals(currentReplicaGroup, replicaGroup.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await RemoveReplicaAsync(replica, cancellationToken);
    }

    private IReadOnlyList<string> CreateRunArguments(
        GraphResource resource,
        string image,
        int replica,
        ResourceOrchestratorReplicaGroup? replicaGroup = null)
    {
        var replicaResourceId = ReplicatedContainerHealthRuntimeConventions.CreateReplicaResourceId(replica);
        var replicaContainerName = ReplicatedContainerHealthRuntimeConventions.CreateReplicaContainerName(replica);
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
            DefaultContainerNetworkName,
            "--network-alias",
            ReplicatedContainerHealthRuntimeConventions.CreateReplicaNetworkAlias(replica)
        };
        var replicaGroupId = replicaGroup?.Id ?? ResolveReplicaGroupId(resource);
        if (!string.IsNullOrWhiteSpace(replicaGroupId))
        {
            arguments.Add("--label");
            arguments.Add($"{ReplicaGroupLabel}={replicaGroupId}");
        }

        var runtimeRevisionId = replicaGroup?.RuntimeRevisionId ?? ResolveRuntimeRevisionId(resource);
        if (!string.IsNullOrWhiteSpace(runtimeRevisionId))
        {
            arguments.Add("--label");
            arguments.Add($"{RuntimeRevisionLabel}={runtimeRevisionId}");
        }

        if (TryResolveHttpEndpoint(resource, out var endpoint))
        {
            arguments.Add("-p");
            arguments.Add(
                $"127.0.0.1:{ReplicatedContainerHealthRuntimeConventions.ResolveReplicaProbePort(configuration, replica, endpoint.Port!.Value).ToString(CultureInfo.InvariantCulture)}:{(endpoint.TargetPort ?? endpoint.Port.Value).ToString(CultureInfo.InvariantCulture)}");
        }

        arguments.Add("-e");
        arguments.Add($"CLOUDSHELL_RESOURCE_ID={replicaResourceId}");
        arguments.Add("-e");
        arguments.Add($"CLOUDSHELL_REPLICA_ORDINAL={replica.ToString(CultureInfo.InvariantCulture)}");
        arguments.Add("-e");
        arguments.Add($"OTEL_SERVICE_NAME=replicated-container-health-api-replica-{replica.ToString(CultureInfo.InvariantCulture)}");
        arguments.Add("-e");
        arguments.Add($"OTEL_RESOURCE_ATTRIBUTES={CreateOtelResourceAttributes(replicaResourceId, replicaContainerName, replicaName, replica, replicaCount)}");
        AddEnvironment(arguments, "CLOUDSHELL_TRACE_INGEST_ENDPOINT", _traceIngestEndpoint);
        AddEnvironment(arguments, "CLOUDSHELL_METRIC_INGEST_ENDPOINT", _metricIngestEndpoint);
        arguments.Add(image);

        return arguments;
    }

    private async Task<string?> GetContainerLabelAsync(
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
            timeout: _statusProbeTimeout);
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
        GraphResource resource,
        CancellationToken cancellationToken,
        bool createWhenMissing = true,
        bool reuseExisting = true,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition>? routingBindings = null)
    {
        if (!TryResolveHttpEndpoint(resource, out var endpoint))
        {
            return;
        }

        await WriteIngressConfigurationAsync(
            resource,
            endpoint,
            routingBindings ?? [],
            cancellationToken);
        if (reuseExisting)
        {
            var ingressStatus = await InspectContainerAsync(
                ReplicatedContainerHealthRuntimeConventions.CreateIngressContainerName(),
                cancellationToken);
            if (ingressStatus == RuntimeContainerStatus.Running)
            {
                return;
            }

            if (IsMaterialized(ingressStatus))
            {
                await commandRunner.RunAsync(
                    "docker",
                    ["start", ReplicatedContainerHealthRuntimeConventions.CreateIngressContainerName()],
                    cancellationToken);
                return;
            }
        }

        if (!createWhenMissing && ResolveReplicas(resource) <= 1)
        {
            return;
        }

        if (ResolveReplicas(resource) <= 1)
        {
            return;
        }

        var hostPort = endpoint.Port!.Value;
        var arguments = new List<string>
        {
            "run",
            "-d",
            "--name",
            ReplicatedContainerHealthRuntimeConventions.CreateIngressContainerName(),
            "--network",
            DefaultContainerNetworkName,
            "-p",
            $"127.0.0.1:{hostPort.ToString(CultureInfo.InvariantCulture)}:{hostPort.ToString(CultureInfo.InvariantCulture)}/tcp",
            "-v",
            $"{Path.GetFullPath(_ingressConfigurationDirectory)}:/etc/traefik/dynamic:ro",
            configuration["ReplicatedContainerHealth:RuntimeIngressImage"] ?? DefaultIngressImage,
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
        int replicaCount,
        CancellationToken cancellationToken)
    {
        var replicas = new InspectedReplica[Math.Max(0, replicaCount)];
        for (var replica = 1; replica <= replicas.Length; replica++)
        {
            replicas[replica - 1] = new InspectedReplica(
                replica,
                await InspectContainerAsync(
                    ReplicatedContainerHealthRuntimeConventions.CreateReplicaContainerName(replica),
                    cancellationToken));
        }

        return replicas;
    }

    private async Task<RuntimeContainerStatus> InspectContainerAsync(
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
            timeout: _statusProbeTimeout);
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
        GraphResource resource,
        NetworkingEndpointRequestValue endpoint,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_ingressConfigurationDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_ingressConfigurationDirectory, "dynamic.yml"),
            CreateIngressConfiguration(resource, endpoint, routingBindings),
            cancellationToken);
    }

    private static string CreateIngressConfiguration(
        GraphResource resource,
        NetworkingEndpointRequestValue endpoint,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings)
    {
        var targetPort = endpoint.TargetPort ?? endpoint.Port!.Value;
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
        for (var replica = 1; replica <= ResolveReplicas(resource); replica++)
        {
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"          - url: \"http://{ReplicatedContainerHealthRuntimeConventions.CreateReplicaNetworkAlias(replica)}:{targetPort.ToString(CultureInfo.InvariantCulture)}\"");
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
        out NetworkingEndpointRequestValue endpoint)
    {
        endpoint = resource.Attributes
            .GetObject<NetworkingEndpointRequestValue[]>(
                ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests)?
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "http", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Protocol, "http", StringComparison.OrdinalIgnoreCase))!;

        return endpoint is { Port: > 0 };
    }

    private static string ResolveImage(GraphResource resource) =>
        resource.Attributes.GetString(ContainerApplicationResourceTypeProvider.Attributes.ContainerImage)
        ?? throw new InvalidOperationException(
            "The container app image must be set before sample runtime can start it.");

    private static string ResolveReplicaGroupId(GraphResource resource) =>
        ResourceOrchestratorReplicaGroups.CreateReplicaGroupId(
            ResourceOrchestratorReplicaGroups.CreateDefaultServiceName(resource.EffectiveResourceId),
            ResolveRuntimeRevisionId(resource));

    private static string ResolveRuntimeRevisionId(GraphResource resource)
    {
        var registry = resource.Attributes.GetString(ContainerApplicationResourceTypeProvider.Attributes.ContainerRegistry);
        if (string.IsNullOrWhiteSpace(registry))
        {
            registry = ContainerRegistryDefaults.Default;
        }

        var image = ResolveImage(resource);
        var revisionKey = $"{registry.Trim()}\n{image.Trim()}";
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(revisionKey), hash);
        return $"rev-img-{Convert.ToHexString(hash[..6]).ToLowerInvariant()}";
    }

    private static int ResolveReplicas(GraphResource resource) =>
        int.TryParse(
            resource.Attributes.GetString(ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas),
            out var replicas)
            ? Math.Max(1, replicas)
            : 1;

    private static string CreateOtelResourceAttributes(
        string replicaResourceId,
        string replicaContainerName,
        string replicaName,
        int replica,
        int replicaCount)
    {
        var replicaOrdinal = replica.ToString(CultureInfo.InvariantCulture);
        var totalReplicas = replicaCount.ToString(CultureInfo.InvariantCulture);
        return string.Join(
            ',',
            CreateOtelAttribute("service.instance.id", replicaResourceId),
            CreateOtelAttribute("cloudshell.resource.id", replicaResourceId),
            CreateOtelAttribute("cloudshell.resource.type", "runtime.container"),
            CreateOtelAttribute(TelemetryAttributeNames.ScopeResourceId, ReplicatedContainerHealthRuntimeConventions.ApiResourceId),
            CreateOtelAttribute(TelemetryAttributeNames.ScopeName, replicaName),
            CreateOtelAttribute(TelemetryAttributeNames.ScopeKind, "runtime"),
            CreateOtelAttribute(TelemetryAttributeNames.RuntimeReplicaOrdinal, replicaOrdinal),
            CreateOtelAttribute(TelemetryAttributeNames.RuntimeReplicaCount, totalReplicas),
            CreateOtelAttribute(TelemetryAttributeNames.RuntimeContainerName, replicaContainerName));
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

    private int ResolveReplicaCleanupLimit(GraphResource resource) =>
        Math.Max(ResolveReplicas(resource), _replicaCleanupLimit);

    private static (string Repository, string Tag) SplitImage(string image)
    {
        var separator = image.LastIndexOf(':');
        if (separator <= 0 || separator == image.Length - 1)
        {
            return (image, "latest");
        }

        return (image[..separator], image[(separator + 1)..]);
    }

    private static ResourceDefinitionDiagnostic RuntimeFailed(
        GraphResource resource,
        Exception exception) =>
        ResourceDefinitionDiagnostic.Error(
            "replicatedContainerHealth.runtimeFailed",
            exception.Message,
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
