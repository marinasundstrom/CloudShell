using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Globalization;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

internal sealed class ReplicatedContainerHealthGraphOnlyContainerAppRuntimeBridge(
    IReplicatedContainerHealthCommandRunner commandRunner,
    IConfiguration configuration,
    IHostEnvironment? hostEnvironment = null) : IReplicatedContainerHealthGraphContainerAppRuntimeBridge
{
    private const string DefaultProjectPath = "samples/ReplicatedContainerHealth/Api/CloudShell.ReplicatedContainerHealth.Api.csproj";
    private readonly string _projectPath = hostEnvironment is null
        ? DefaultProjectPath
        : Path.Combine(hostEnvironment.ContentRootPath, "Api", "CloudShell.ReplicatedContainerHealth.Api.csproj");
    private readonly object _statusGate = new();
    private readonly TimeSpan _statusProbeTimeout = TimeSpan.FromMilliseconds(
        configuration.GetValue<int?>("ReplicatedContainerHealth:GraphOnlyStatusProbeTimeoutMilliseconds") ?? 50);
    private readonly TimeSpan _statusCacheDuration = TimeSpan.FromMilliseconds(
        configuration.GetValue<int?>("ReplicatedContainerHealth:GraphOnlyStatusCacheMilliseconds") ?? 2_000);
    private readonly int _replicaCleanupLimit = Math.Max(
        1,
        configuration.GetValue<int?>("ReplicatedContainerHealth:GraphOnlyReplicaCleanupLimit") ?? 10);
    private ContainerApplicationRuntimeStatus? _cachedStatus;
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

        var status = ResolveStatus(resource);
        lock (_statusGate)
        {
            _cachedStatus = status;
            _cachedStatusTimestamp = now;
        }

        return status;
    }

    private ContainerApplicationRuntimeStatus ResolveStatus(GraphResource resource)
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
                    ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateReplicaContainerName(replica)
                ],
                throwOnError: false,
                timeout: _statusProbeTimeout);
            if (result.ExitCode == ReplicatedContainerHealthCommandResult.TimeoutExitCode)
            {
                return ContainerApplicationRuntimeStatus.Unknown;
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
                    return ContainerApplicationRuntimeStatus.Unknown;
            }
        }

        if (running == replicas)
        {
            return ContainerApplicationRuntimeStatus.Running;
        }

        return stopped == replicas
            ? ContainerApplicationRuntimeStatus.Stopped
            : ContainerApplicationRuntimeStatus.Unknown;
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
                        $"The ReplicatedContainerHealth sample does not map graph operation '{operationId}' to the graph-only container runtime.");
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
        if (GetStatus(resource) == ContainerApplicationRuntimeStatus.Running)
        {
            return await ExecuteLifecycleAsync(
                resource,
                ContainerApplicationResourceTypeProvider.Operations.Restart,
                cancellationToken);
        }

        return [];
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
        if (GetStatus(resource) == ContainerApplicationRuntimeStatus.Running)
        {
            return await ExecuteLifecycleAsync(
                resource,
                ContainerApplicationResourceTypeProvider.Operations.Restart,
                cancellationToken);
        }

        return [];
    }

    private async Task StartAsync(
        GraphResource resource,
        CancellationToken cancellationToken,
        bool cleanExistingReplicas = true)
    {
        var image = ResolveImage(resource);
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

        var replicas = ResolveReplicas(resource);
        if (cleanExistingReplicas)
        {
            await RemoveReplicasAsync(ResolveReplicaCleanupLimit(resource), cancellationToken);
        }

        for (var replica = 1; replica <= replicas; replica++)
        {
            await commandRunner.RunAsync(
                "docker",
                CreateRunArguments(resource, image, replica),
                cancellationToken);
        }
    }

    private async Task RemoveAsync(
        GraphResource resource,
        CancellationToken cancellationToken) =>
        await RemoveReplicasAsync(ResolveReplicaCleanupLimit(resource), cancellationToken);

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
                ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateReplicaContainerName(replica)
            ],
            cancellationToken,
            throwOnError: false);

    private IReadOnlyList<string> CreateRunArguments(
        GraphResource resource,
        string image,
        int replica)
    {
        var replicaResourceId = ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateReplicaResourceId(replica);
        var replicaContainerName = ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateReplicaContainerName(replica);
        var replicaName = $"Replica {replica.ToString(CultureInfo.InvariantCulture)}";
        var replicaCount = ResolveReplicas(resource);
        var arguments = new List<string>
        {
            "run",
            "-d",
            "--name",
            replicaContainerName
        };

        if (replica == 1 && TryResolveHttpEndpoint(resource, out var endpoint))
        {
            arguments.Add("-p");
            arguments.Add($"127.0.0.1:{endpoint.Port!.Value.ToString(CultureInfo.InvariantCulture)}:{(endpoint.TargetPort ?? endpoint.Port.Value).ToString(CultureInfo.InvariantCulture)}");
        }

        if (TryResolveHttpEndpoint(resource, out endpoint))
        {
            arguments.Add("-p");
            arguments.Add(
                $"127.0.0.1:{ReplicatedContainerHealthGraphOnlyRuntimeConventions.ResolveReplicaProbePort(configuration, replica, endpoint.Port!.Value).ToString(CultureInfo.InvariantCulture)}:{(endpoint.TargetPort ?? endpoint.Port.Value).ToString(CultureInfo.InvariantCulture)}");
        }

        arguments.Add("-e");
        arguments.Add($"CLOUDSHELL_RESOURCE_ID={replicaResourceId}");
        arguments.Add("-e");
        arguments.Add($"CLOUDSHELL_REPLICA_ORDINAL={replica.ToString(CultureInfo.InvariantCulture)}");
        arguments.Add("-e");
        arguments.Add($"OTEL_SERVICE_NAME=replicated-container-health-graph-api-replica-{replica.ToString(CultureInfo.InvariantCulture)}");
        arguments.Add("-e");
        arguments.Add($"OTEL_RESOURCE_ATTRIBUTES={CreateOtelResourceAttributes(replicaResourceId, replicaContainerName, replicaName, replica, replicaCount)}");
        AddEnvironment(arguments, "CLOUDSHELL_TRACE_INGEST_ENDPOINT", "Observability:TraceIngestEndpoint");
        AddEnvironment(arguments, "CLOUDSHELL_METRIC_INGEST_ENDPOINT", "Observability:MetricIngestEndpoint");
        arguments.Add(image);

        return arguments;
    }

    private void AddEnvironment(
        List<string> arguments,
        string name,
        string configurationKey)
    {
        var value = configuration[configurationKey];
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
            "The graph container app image must be set before graph-only runtime can start it.");

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
            CreateOtelAttribute(TelemetryAttributeNames.ScopeResourceId, ReplicatedContainerHealthGraphOnlyRuntimeConventions.GraphApiResourceId),
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
            "replicatedContainerHealth.graphOnlyRuntimeFailed",
            exception.Message,
            resource.EffectiveResourceId);
}

internal interface IReplicatedContainerHealthCommandRunner
{
    ReplicatedContainerHealthCommandResult Run(
        string fileName,
        IReadOnlyList<string> arguments,
        bool throwOnError = true,
        TimeSpan? timeout = null);

    Task<ReplicatedContainerHealthCommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true,
        TimeSpan? timeout = null);
}

internal sealed record ReplicatedContainerHealthCommandResult(
    int ExitCode,
    string Output,
    string Error)
{
    public const int TimeoutExitCode = -1;
}

internal sealed class ProcessReplicatedContainerHealthCommandRunner :
    IReplicatedContainerHealthCommandRunner
{
    public ReplicatedContainerHealthCommandResult Run(
        string fileName,
        IReadOnlyList<string> arguments,
        bool throwOnError = true,
        TimeSpan? timeout = null) =>
        RunAsync(fileName, arguments, CancellationToken.None, throwOnError, timeout)
            .GetAwaiter()
            .GetResult();

    public async Task<ReplicatedContainerHealthCommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true,
        TimeSpan? timeout = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException($"Command '{fileName}' could not be started.");
        using var timeoutCancellation = timeout is null
            ? null
            : new CancellationTokenSource(timeout.Value);
        using var linkedCancellation = timeoutCancellation is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);
        var waitCancellationToken = linkedCancellation?.Token ?? cancellationToken;
        var outputTask = process.StandardOutput.ReadToEndAsync(waitCancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(waitCancellationToken);
        try
        {
            await process.WaitForExitAsync(waitCancellationToken);
        }
        catch (OperationCanceledException) when (
            timeoutCancellation?.IsCancellationRequested == true &&
            !cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            return new ReplicatedContainerHealthCommandResult(
                ReplicatedContainerHealthCommandResult.TimeoutExitCode,
                string.Empty,
                $"Command '{fileName} {string.Join(' ', arguments)}' timed out.");
        }

        var result = new ReplicatedContainerHealthCommandResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
        if (throwOnError && result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Command '{fileName} {string.Join(' ', arguments)}' failed with exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}: {result.Error}");
        }

        return result;
    }
}
