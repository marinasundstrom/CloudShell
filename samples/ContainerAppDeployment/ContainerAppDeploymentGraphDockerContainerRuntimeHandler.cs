using System.Diagnostics;
using System.Globalization;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

internal sealed class ContainerAppDeploymentGraphDockerContainerRuntimeHandler :
    IDockerContainerRuntimeHandler
{
    internal const string GraphRegistryResourceId = "docker.container:graph-sample-registry";
    internal const string GraphRegistryContainerName = "cloudshell-container-app-deployment-graph-registry";
    private readonly IContainerAppDeploymentDockerCommandRunner _docker;

    public ContainerAppDeploymentGraphDockerContainerRuntimeHandler()
        : this(new ProcessContainerAppDeploymentDockerCommandRunner())
    {
    }

    internal ContainerAppDeploymentGraphDockerContainerRuntimeHandler(
        IContainerAppDeploymentDockerCommandRunner docker)
    {
        ArgumentNullException.ThrowIfNull(docker);

        _docker = docker;
    }

    public DockerContainerRuntimeStatus GetStatus(GraphResource resource)
    {
        if (!IsGraphRegistry(resource))
        {
            return DockerContainerRuntimeStatus.Unknown;
        }

        var result = _docker.Run(
            ["container", "inspect", "--format", "{{.State.Status}}", GraphRegistryContainerName],
            throwOnError: false);
        if (result.ExitCode != 0)
        {
            return DockerContainerRuntimeStatus.Stopped;
        }

        return result.Output.Trim().ToLowerInvariant() switch
        {
            "running" => DockerContainerRuntimeStatus.Running,
            "paused" => DockerContainerRuntimeStatus.Paused,
            "created" or "exited" or "dead" => DockerContainerRuntimeStatus.Stopped,
            _ => DockerContainerRuntimeStatus.Unknown
        };
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        GraphResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsGraphRegistry(resource))
        {
            return [];
        }

        try
        {
            switch (operationId.ToString())
            {
                case "start":
                    await StartRegistryAsync(resource, cancellationToken);
                    break;
                case "stop":
                    await RemoveRegistryAsync(cancellationToken);
                    break;
                case "restart":
                    await RemoveRegistryAsync(cancellationToken);
                    await StartRegistryAsync(resource, cancellationToken);
                    break;
                case "pause":
                    await _docker.RunAsync(["pause", GraphRegistryContainerName], cancellationToken, throwOnError: false);
                    break;
                case "docker.unpause":
                    await _docker.RunAsync(["unpause", GraphRegistryContainerName], cancellationToken, throwOnError: false);
                    break;
                default:
                    throw new NotSupportedException(
                        $"The ContainerAppDeployment sample does not map graph Docker container operation '{operationId}' to the registry runtime.");
            }

            return [];
        }
        catch (Exception exception)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "containerAppDeployment.dockerContainer.runtimeLifecycleFailed",
                    exception.Message,
                    resource.EffectiveResourceId)
            ];
        }
    }

    private async Task StartRegistryAsync(
        GraphResource resource,
        CancellationToken cancellationToken)
    {
        var status = _docker.Run(
            ["container", "inspect", "--format", "{{.State.Status}}", GraphRegistryContainerName],
            throwOnError: false);
        if (status.ExitCode == 0)
        {
            if (string.Equals(status.Output.Trim(), "running", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await _docker.RunAsync(["start", GraphRegistryContainerName], cancellationToken);
            return;
        }

        var image = resource.Attributes.GetString(
            DockerContainerResourceTypeProvider.Attributes.ContainerImage);
        if (string.IsNullOrWhiteSpace(image))
        {
            throw new InvalidOperationException("The graph registry container image must be set before it can be started.");
        }

        var port = ResolveRegistryPort(resource);
        await _docker.RunAsync(
            [
                "run",
                "-d",
                "--name",
                GraphRegistryContainerName,
                "-p",
                $"127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}:5000",
                image
            ],
            cancellationToken);
    }

    private async Task RemoveRegistryAsync(CancellationToken cancellationToken) =>
        await _docker.RunAsync(["rm", "-f", GraphRegistryContainerName], cancellationToken, throwOnError: false);

    private static int ResolveRegistryPort(GraphResource resource)
    {
        var registry = resource.Attributes.GetString(
            DockerContainerResourceTypeProvider.Attributes.ContainerRegistry);
        if (string.IsNullOrWhiteSpace(registry))
        {
            throw new InvalidOperationException("The graph registry container registry address must be set before it can be started.");
        }

        var value = registry.Contains("://", StringComparison.Ordinal)
            ? registry
            : $"http://{registry}";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Port <= 0)
        {
            throw new InvalidOperationException(
                $"The graph registry container registry address '{registry}' does not include a usable port.");
        }

        return uri.Port;
    }

    private static bool IsGraphRegistry(GraphResource resource) =>
        string.Equals(resource.EffectiveResourceId, GraphRegistryResourceId, StringComparison.OrdinalIgnoreCase);
}

internal interface IContainerAppDeploymentDockerCommandRunner
{
    ContainerAppDeploymentDockerCommandResult Run(
        IReadOnlyList<string> arguments,
        bool throwOnError = true);

    Task<ContainerAppDeploymentDockerCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true);
}

internal sealed class ProcessContainerAppDeploymentDockerCommandRunner :
    IContainerAppDeploymentDockerCommandRunner
{
    public ContainerAppDeploymentDockerCommandResult Run(
        IReadOnlyList<string> arguments,
        bool throwOnError = true) =>
        RunAsync(arguments, CancellationToken.None, throwOnError)
            .GetAwaiter()
            .GetResult();

    public async Task<ContainerAppDeploymentDockerCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true)
    {
        var startInfo = new ProcessStartInfo("docker")
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
            throw new InvalidOperationException("Docker command could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var result = new ContainerAppDeploymentDockerCommandResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
        if (throwOnError && result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Docker command 'docker {string.Join(' ', arguments)}' failed with exit code {result.ExitCode}: {result.Error}");
        }

        return result;
    }
}

internal sealed record ContainerAppDeploymentDockerCommandResult(
    int ExitCode,
    string Output,
    string Error);
