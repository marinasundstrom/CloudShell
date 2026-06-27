using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using System.Diagnostics;
using System.Globalization;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

internal sealed class ReplicatedContainerHealthGraphOnlyContainerAppRuntimeBridge(
    IReplicatedContainerHealthCommandRunner commandRunner,
    IConfiguration configuration) : IReplicatedContainerHealthGraphContainerAppRuntimeBridge
{
    private const string ResourceId = "application.container-app:graph-api";
    private const string ProjectPath = "samples/ReplicatedContainerHealth/Api/CloudShell.ReplicatedContainerHealth.Api.csproj";
    private ContainerApplicationRuntimeStatus _status = ContainerApplicationRuntimeStatus.Unknown;

    public ContainerApplicationRuntimeStatus GetStatus(GraphResource resource) =>
        _status;

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
                    _status = ContainerApplicationRuntimeStatus.Running;
                    break;
                case ResourceActionIds.Stop:
                    await RemoveAsync(resource, cancellationToken);
                    _status = ContainerApplicationRuntimeStatus.Stopped;
                    break;
                case ResourceActionIds.Restart:
                    await RemoveAsync(resource, cancellationToken);
                    await StartAsync(resource, cancellationToken);
                    _status = ContainerApplicationRuntimeStatus.Running;
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
        if (_status == ContainerApplicationRuntimeStatus.Running)
        {
            return await ExecuteLifecycleAsync(
                resource,
                ContainerApplicationResourceTypeProvider.Operations.Restart,
                cancellationToken);
        }

        return [];
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        if (_status == ContainerApplicationRuntimeStatus.Running)
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
        CancellationToken cancellationToken)
    {
        var image = ResolveImage(resource);
        var (repository, tag) = SplitImage(image);

        await commandRunner.RunAsync(
            "dotnet",
            [
                "publish",
                ProjectPath,
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
        for (var replica = 1; replica <= replicas; replica++)
        {
            await RemoveReplicaAsync(replica, cancellationToken);
            await commandRunner.RunAsync(
                "docker",
                CreateRunArguments(resource, image, replica),
                cancellationToken);
        }
    }

    private async Task RemoveAsync(
        GraphResource resource,
        CancellationToken cancellationToken)
    {
        for (var replica = 1; replica <= ResolveReplicas(resource); replica++)
        {
            await RemoveReplicaAsync(replica, cancellationToken);
        }
    }

    private Task RemoveReplicaAsync(
        int replica,
        CancellationToken cancellationToken) =>
        commandRunner.RunAsync(
            "docker",
            ["rm", "-f", CreateReplicaContainerName(replica)],
            cancellationToken,
            throwOnError: false);

    private IReadOnlyList<string> CreateRunArguments(
        GraphResource resource,
        string image,
        int replica)
    {
        var arguments = new List<string>
        {
            "run",
            "-d",
            "--name",
            CreateReplicaContainerName(replica)
        };

        if (replica == 1 && TryResolveHttpEndpoint(resource, out var endpoint))
        {
            arguments.Add("-p");
            arguments.Add($"127.0.0.1:{endpoint.Port!.Value.ToString(CultureInfo.InvariantCulture)}:{(endpoint.TargetPort ?? endpoint.Port.Value).ToString(CultureInfo.InvariantCulture)}");
        }

        arguments.Add("-e");
        arguments.Add($"CLOUDSHELL_RESOURCE_ID={ResourceId}");
        arguments.Add("-e");
        arguments.Add($"CLOUDSHELL_REPLICA_ORDINAL={replica.ToString(CultureInfo.InvariantCulture)}");
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

    private static (string Repository, string Tag) SplitImage(string image)
    {
        var separator = image.LastIndexOf(':');
        if (separator <= 0 || separator == image.Length - 1)
        {
            return (image, "latest");
        }

        return (image[..separator], image[(separator + 1)..]);
    }

    private static string CreateReplicaContainerName(int replica) =>
        $"cloudshell-replicated-health-graph-api-replica-{replica.ToString(CultureInfo.InvariantCulture)}";

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
    Task<ReplicatedContainerHealthCommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true);
}

internal sealed record ReplicatedContainerHealthCommandResult(
    int ExitCode,
    string Output,
    string Error);

internal sealed class ProcessReplicatedContainerHealthCommandRunner :
    IReplicatedContainerHealthCommandRunner
{
    public async Task<ReplicatedContainerHealthCommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true)
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
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
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
