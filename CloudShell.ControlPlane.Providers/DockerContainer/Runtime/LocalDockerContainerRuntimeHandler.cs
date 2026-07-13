using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using System.ComponentModel;

namespace CloudShell.ControlPlane.Providers;

public sealed class LocalDockerContainerRuntimeOptions
{
    private readonly Dictionary<string, LocalDockerContainerDefinition> containers =
        new(StringComparer.OrdinalIgnoreCase);

    public IDictionary<string, LocalDockerContainerDefinition> Containers => containers;

    public LocalDockerContainerRuntimeOptions AddContainer(
        string resourceId,
        string containerName,
        Action<LocalDockerContainerDefinition>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);

        var definition = new LocalDockerContainerDefinition
        {
            ContainerName = containerName
        };
        configure?.Invoke(definition);
        containers[resourceId] = definition;
        return this;
    }
}

public sealed class LocalDockerContainerDefinition
{
    public string ContainerName { get; set; } = string.Empty;

    public string BindAddress { get; set; } = "127.0.0.1";

    public int? HostPort { get; set; }

    public int? TargetPort { get; set; }

    public TimeSpan StatusProbeTimeout { get; set; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan StatusCacheDuration { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan RemoveTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public IList<string> AdditionalRunArguments { get; } = [];
}

public sealed class LocalDockerContainerRuntimeHandler(
    ILocalDockerContainerCommandRunner docker,
    IOptions<LocalDockerContainerRuntimeOptions> options) : IDockerContainerRuntimeHandler
{
    private const string RuntimeLifecycleFailedDiagnosticCode =
        "docker.container.localRuntimeLifecycleFailed";
    private readonly LocalDockerContainerRuntimeOptions options = options.Value;
    private readonly object statusGate = new();
    private readonly Dictionary<string, (DockerContainerRuntimeStatus Status, DateTimeOffset Timestamp)> statusCache =
        new(StringComparer.OrdinalIgnoreCase);

    public DockerContainerRuntimeStatus GetStatus(Resource resource)
    {
        if (!TryGetDefinition(resource, out var definition))
        {
            return DockerContainerRuntimeStatus.Unknown;
        }

        var now = DateTimeOffset.UtcNow;
        lock (statusGate)
        {
            if (statusCache.TryGetValue(resource.EffectiveResourceId, out var cached) &&
                now - cached.Timestamp <= definition.StatusCacheDuration)
            {
                return cached.Status;
            }
        }

        var status = ResolveStatus(definition);
        lock (statusGate)
        {
            statusCache[resource.EffectiveResourceId] = (status, now);
        }

        return status;
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetDefinition(resource, out var definition))
        {
            return [];
        }

        try
        {
            switch (operationId.Value)
            {
                case ResourceActionIds.Start:
                    await StartAsync(resource, definition, cancellationToken);
                    ClearStatusCache(resource);
                    break;
                case ResourceActionIds.Stop:
                    await RemoveAsync(definition, cancellationToken);
                    ClearStatusCache(resource);
                    break;
                case ResourceActionIds.Restart:
                    await RemoveAsync(definition, cancellationToken);
                    await StartAsync(resource, definition, cancellationToken);
                    ClearStatusCache(resource);
                    break;
                case ResourceActionIds.Pause:
                    await docker.RunAsync(
                        ["pause", definition.ContainerName],
                        cancellationToken,
                        throwOnError: false);
                    ClearStatusCache(resource);
                    break;
                case "docker.unpause":
                    await docker.RunAsync(
                        ["unpause", definition.ContainerName],
                        cancellationToken,
                        throwOnError: false);
                    ClearStatusCache(resource);
                    break;
                default:
                    throw new NotSupportedException(
                        $"The local Docker container runtime does not support operation '{operationId}'.");
            }

            return [];
        }
        catch (Exception exception)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    RuntimeLifecycleFailedDiagnosticCode,
                    exception.Message,
                    resource.EffectiveResourceId)
            ];
        }
    }

    private DockerContainerRuntimeStatus ResolveStatus(LocalDockerContainerDefinition definition)
    {
        var result = docker.Run(
            ["container", "inspect", "--format", "{{.State.Status}}", definition.ContainerName],
            throwOnError: false,
            timeout: definition.StatusProbeTimeout);
        if (result.ExitCode == LocalDockerContainerCommandResult.TimeoutExitCode)
        {
            return DockerContainerRuntimeStatus.Unknown;
        }

        if (result.ExitCode == LocalDockerContainerCommandResult.UnavailableExitCode)
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

    private async Task StartAsync(
        Resource resource,
        LocalDockerContainerDefinition definition,
        CancellationToken cancellationToken)
    {
        var status = docker.Run(
            ["container", "inspect", "--format", "{{.State.Status}}", definition.ContainerName],
            throwOnError: false);
        if (status.ExitCode == 0)
        {
            if (string.Equals(status.Output.Trim(), "running", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await docker.RunAsync(["start", definition.ContainerName], cancellationToken);
            return;
        }

        var image = resource.Attributes.GetString(
            DockerContainerResourceTypeProvider.Attributes.ContainerImage);
        if (string.IsNullOrWhiteSpace(image))
        {
            throw new InvalidOperationException("The Docker container image must be set before it can be started.");
        }

        var arguments = new List<string>
        {
            "run",
            "-d",
            "--name",
            definition.ContainerName
        };
        AddPortBinding(arguments, resource, definition);
        arguments.AddRange(definition.AdditionalRunArguments);
        arguments.Add(image);

        await docker.RunAsync(arguments, cancellationToken);
    }

    private static void AddPortBinding(
        List<string> arguments,
        Resource resource,
        LocalDockerContainerDefinition definition)
    {
        if (definition.TargetPort is null)
        {
            return;
        }

        var hostPort = definition.HostPort ?? ResolveHostPort(resource);
        arguments.Add("-p");
        arguments.Add(
            $"{definition.BindAddress}:{hostPort.ToString(CultureInfo.InvariantCulture)}:{definition.TargetPort.Value.ToString(CultureInfo.InvariantCulture)}");
    }

    private static int ResolveHostPort(Resource resource)
    {
        var registry = resource.Attributes.GetString(
            DockerContainerResourceTypeProvider.Attributes.ContainerRegistry);
        if (string.IsNullOrWhiteSpace(registry))
        {
            throw new InvalidOperationException(
                "The Docker container registry address must include a port or an explicit host port must be configured.");
        }

        var value = registry.Contains("://", StringComparison.Ordinal)
            ? registry
            : $"http://{registry}";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Port <= 0)
        {
            throw new InvalidOperationException(
                $"The Docker container registry address '{registry}' does not include a usable port.");
        }

        return uri.Port;
    }

    private async Task RemoveAsync(
        LocalDockerContainerDefinition definition,
        CancellationToken cancellationToken) =>
        await docker.RunAsync(
            ["rm", "-f", definition.ContainerName],
            cancellationToken,
            throwOnError: false,
            timeout: definition.RemoveTimeout);

    private void ClearStatusCache(Resource resource)
    {
        lock (statusGate)
        {
            statusCache.Remove(resource.EffectiveResourceId);
        }
    }

    private bool TryGetDefinition(
        Resource resource,
        out LocalDockerContainerDefinition definition) =>
        options.Containers.TryGetValue(resource.EffectiveResourceId, out definition!);
}

public interface ILocalDockerContainerCommandRunner
{
    LocalDockerContainerCommandResult Run(
        IReadOnlyList<string> arguments,
        bool throwOnError = true,
        TimeSpan? timeout = null);

    Task<LocalDockerContainerCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true,
        TimeSpan? timeout = null);
}

public sealed class ProcessLocalDockerContainerCommandRunner(
    IContainerHostCommandPlatform commandPlatform) : ILocalDockerContainerCommandRunner
{
    public LocalDockerContainerCommandResult Run(
        IReadOnlyList<string> arguments,
        bool throwOnError = true,
        TimeSpan? timeout = null) =>
        RunAsync(arguments, CancellationToken.None, throwOnError, timeout)
            .GetAwaiter()
            .GetResult();

    public async Task<LocalDockerContainerCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true,
        TimeSpan? timeout = null)
    {
        var plan = commandPlatform.CreatePlan();
        if (!plan.IsAvailable)
        {
            if (throwOnError)
            {
                throw new InvalidOperationException(plan.UnavailableReason);
            }

            return new(
                LocalDockerContainerCommandResult.UnavailableExitCode,
                string.Empty,
                plan.UnavailableReason ?? "Container runtime command is unavailable.");
        }

        var startInfo = plan.CreateStartInfo(arguments);

        using var timeoutCancellation = timeout is null
            ? null
            : new CancellationTokenSource(timeout.Value);
        using var linkedCancellation = timeoutCancellation is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);
        var waitCancellationToken = linkedCancellation?.Token ?? cancellationToken;
        try
        {
            using var process = Process.Start(startInfo) ??
                throw new InvalidOperationException("Docker command could not be started.");
            using var cancellationRegistration = waitCancellationToken.Register(
                static state => KillProcessTree((Process)state!),
                process);
            var outputTask = process.StandardOutput.ReadToEndAsync(waitCancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(waitCancellationToken);
            await process.WaitForExitAsync(waitCancellationToken);
            var result = new LocalDockerContainerCommandResult(
                process.ExitCode,
                await outputTask,
                await errorTask);
            if (throwOnError && result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Docker command '{startInfo.FileName} {string.Join(' ', arguments)}' failed with exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}: {result.Error}");
            }

            return result;
        }
        catch (OperationCanceledException) when (
            timeoutCancellation?.IsCancellationRequested == true &&
            !cancellationToken.IsCancellationRequested)
        {
            return new(
                LocalDockerContainerCommandResult.TimeoutExitCode,
                string.Empty,
                $"Docker command '{startInfo.FileName} {string.Join(' ', arguments)}' timed out.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            if (throwOnError)
            {
                throw;
            }

            return new(
                LocalDockerContainerCommandResult.UnavailableExitCode,
                string.Empty,
                exception.Message);
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit((int)TimeSpan.FromSeconds(1).TotalMilliseconds);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
        }
    }
}

public sealed record LocalDockerContainerCommandResult(
    int ExitCode,
    string Output,
    string Error)
{
    public const int TimeoutExitCode = -1;

    public const int UnavailableExitCode = -2;
}
