using System.Diagnostics;
using System.Globalization;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using ResourceModelResource = CloudShell.ResourceDefinitions.Resource;

internal sealed class ContainerAppDeploymentDockerContainerRuntimeHandler :
    IDockerContainerRuntimeHandler
{
    internal const string RegistryResourceId = "docker.container:sample-registry";
    internal const string RegistryContainerName = "cloudshell-container-app-deployment-registry";
    private static readonly TimeSpan StatusProbeTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan StatusCacheDuration = TimeSpan.FromSeconds(2);
    private readonly IContainerAppDeploymentDockerCommandRunner _docker;
    private readonly object _statusGate = new();
    private DockerContainerRuntimeStatus? _cachedStatus;
    private DateTimeOffset _cachedStatusTimestamp;

    public ContainerAppDeploymentDockerContainerRuntimeHandler()
        : this(new ProcessContainerAppDeploymentDockerCommandRunner())
    {
    }

    internal ContainerAppDeploymentDockerContainerRuntimeHandler(
        IContainerAppDeploymentDockerCommandRunner docker)
    {
        ArgumentNullException.ThrowIfNull(docker);

        _docker = docker;
    }

    public DockerContainerRuntimeStatus GetStatus(ResourceModelResource resource)
    {
        if (!IsSampleRegistry(resource))
        {
            return DockerContainerRuntimeStatus.Unknown;
        }

        var now = DateTimeOffset.UtcNow;
        lock (_statusGate)
        {
            if (_cachedStatus is not null &&
                now - _cachedStatusTimestamp <= StatusCacheDuration)
            {
                return _cachedStatus.Value;
            }
        }

        var status = ResolveStatus();
        lock (_statusGate)
        {
            _cachedStatus = status;
            _cachedStatusTimestamp = now;
        }

        return status;
    }

    private DockerContainerRuntimeStatus ResolveStatus()
    {
        var result = _docker.Run(
            ["container", "inspect", "--format", "{{.State.Status}}", RegistryContainerName],
            throwOnError: false,
            timeout: StatusProbeTimeout);
        if (result.ExitCode == ContainerAppDeploymentDockerCommandResult.TimeoutExitCode)
        {
            return DockerContainerRuntimeStatus.Unknown;
        }

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
        ResourceModelResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsSampleRegistry(resource))
        {
            return [];
        }

        try
        {
            switch (operationId.ToString())
            {
                case "start":
                    await StartRegistryAsync(resource, cancellationToken);
                    ClearStatusCache();
                    break;
                case "stop":
                    await RemoveRegistryAsync(cancellationToken);
                    ClearStatusCache();
                    break;
                case "restart":
                    await RemoveRegistryAsync(cancellationToken);
                    await StartRegistryAsync(resource, cancellationToken);
                    ClearStatusCache();
                    break;
                case "pause":
                    await _docker.RunAsync(["pause", RegistryContainerName], cancellationToken, throwOnError: false);
                    ClearStatusCache();
                    break;
                case "docker.unpause":
                    await _docker.RunAsync(["unpause", RegistryContainerName], cancellationToken, throwOnError: false);
                    ClearStatusCache();
                    break;
                default:
                    throw new NotSupportedException(
                        $"The ContainerAppDeployment sample does not map Docker container operation '{operationId}' to the registry runtime.");
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

    private void ClearStatusCache()
    {
        lock (_statusGate)
        {
            _cachedStatus = null;
            _cachedStatusTimestamp = default;
        }
    }

    private async Task StartRegistryAsync(
        ResourceModelResource resource,
        CancellationToken cancellationToken)
    {
        var status = _docker.Run(
            ["container", "inspect", "--format", "{{.State.Status}}", RegistryContainerName],
            throwOnError: false);
        if (status.ExitCode == 0)
        {
            if (string.Equals(status.Output.Trim(), "running", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await _docker.RunAsync(["start", RegistryContainerName], cancellationToken);
            return;
        }

        var image = resource.Attributes.GetString(
            DockerContainerResourceTypeProvider.Attributes.ContainerImage);
        if (string.IsNullOrWhiteSpace(image))
        {
            throw new InvalidOperationException("The registry container image must be set before it can be started.");
        }

        var port = ResolveRegistryPort(resource);
        await _docker.RunAsync(
            [
                "run",
                "-d",
                "--name",
                RegistryContainerName,
                "-p",
                $"127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}:5000",
                image
            ],
            cancellationToken);
    }

    private async Task RemoveRegistryAsync(CancellationToken cancellationToken) =>
        await _docker.RunAsync(["rm", "-f", RegistryContainerName], cancellationToken, throwOnError: false);

    private static int ResolveRegistryPort(ResourceModelResource resource)
    {
        var registry = resource.Attributes.GetString(
            DockerContainerResourceTypeProvider.Attributes.ContainerRegistry);
        if (string.IsNullOrWhiteSpace(registry))
        {
            throw new InvalidOperationException("The registry container registry address must be set before it can be started.");
        }

        var value = registry.Contains("://", StringComparison.Ordinal)
            ? registry
            : $"http://{registry}";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Port <= 0)
        {
            throw new InvalidOperationException(
                $"The registry container registry address '{registry}' does not include a usable port.");
        }

        return uri.Port;
    }

    private static bool IsSampleRegistry(ResourceModelResource resource) =>
        string.Equals(resource.EffectiveResourceId, RegistryResourceId, StringComparison.OrdinalIgnoreCase);
}

internal interface IContainerAppDeploymentDockerCommandRunner
{
    ContainerAppDeploymentDockerCommandResult Run(
        IReadOnlyList<string> arguments,
        bool throwOnError = true,
        TimeSpan? timeout = null);

    Task<ContainerAppDeploymentDockerCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true,
        TimeSpan? timeout = null);
}

internal sealed class ProcessContainerAppDeploymentDockerCommandRunner :
    IContainerAppDeploymentDockerCommandRunner
{
    public ContainerAppDeploymentDockerCommandResult Run(
        IReadOnlyList<string> arguments,
        bool throwOnError = true,
        TimeSpan? timeout = null) =>
        RunAsync(arguments, CancellationToken.None, throwOnError, timeout)
            .GetAwaiter()
            .GetResult();

    public async Task<ContainerAppDeploymentDockerCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true,
        TimeSpan? timeout = null)
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

            return new ContainerAppDeploymentDockerCommandResult(
                ContainerAppDeploymentDockerCommandResult.TimeoutExitCode,
                string.Empty,
                $"Docker command 'docker {string.Join(' ', arguments)}' timed out.");
        }

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
    string Error)
{
    public const int TimeoutExitCode = -1;
}
