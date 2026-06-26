using System.Diagnostics;
using System.Globalization;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

internal sealed class ContainerAppDeploymentGraphDockerContainerRuntimeHandler :
    IDockerContainerRuntimeHandler
{
    private const string GraphRegistryResourceId = "docker.container:graph-sample-registry";
    private const string GraphRegistryContainerName = "cloudshell-container-app-deployment-graph-registry";

    public DockerContainerRuntimeStatus GetStatus(GraphResource resource)
    {
        if (!IsGraphRegistry(resource))
        {
            return DockerContainerRuntimeStatus.Unknown;
        }

        var result = RunDocker(
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
                    await RunDockerAsync(["pause", GraphRegistryContainerName], cancellationToken, throwOnError: false);
                    break;
                case "docker.unpause":
                    await RunDockerAsync(["unpause", GraphRegistryContainerName], cancellationToken, throwOnError: false);
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

    private static async Task StartRegistryAsync(
        GraphResource resource,
        CancellationToken cancellationToken)
    {
        var status = RunDocker(
            ["container", "inspect", "--format", "{{.State.Status}}", GraphRegistryContainerName],
            throwOnError: false);
        if (status.ExitCode == 0)
        {
            if (string.Equals(status.Output.Trim(), "running", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await RunDockerAsync(["start", GraphRegistryContainerName], cancellationToken);
            return;
        }

        var image = resource.Attributes.GetString(
            DockerContainerResourceTypeProvider.Attributes.ContainerImage);
        if (string.IsNullOrWhiteSpace(image))
        {
            throw new InvalidOperationException("The graph registry container image must be set before it can be started.");
        }

        var port = ResolveRegistryPort(resource);
        await RunDockerAsync(
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

    private static async Task RemoveRegistryAsync(CancellationToken cancellationToken) =>
        await RunDockerAsync(["rm", "-f", GraphRegistryContainerName], cancellationToken, throwOnError: false);

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

    private static DockerCommandResult RunDocker(
        IReadOnlyList<string> arguments,
        bool throwOnError = true) =>
        RunDockerAsync(arguments, CancellationToken.None, throwOnError)
            .GetAwaiter()
            .GetResult();

    private static async Task<DockerCommandResult> RunDockerAsync(
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
        var result = new DockerCommandResult(
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

    private sealed record DockerCommandResult(
        int ExitCode,
        string Output,
        string Error);
}
