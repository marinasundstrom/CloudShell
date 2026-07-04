using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CloudShell.ControlPlane.Providers;

public sealed class LocalRabbitMQDockerRuntimeOptions
{
    private readonly Dictionary<string, LocalRabbitMQDockerDefinition> brokers =
        new(StringComparer.OrdinalIgnoreCase);

    public IDictionary<string, LocalRabbitMQDockerDefinition> Brokers => brokers;

    public LocalRabbitMQDockerRuntimeOptions AddBroker(
        string resourceId,
        string containerName,
        Action<LocalRabbitMQDockerDefinition>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);

        var definition = new LocalRabbitMQDockerDefinition
        {
            ContainerName = containerName
        };
        configure?.Invoke(definition);
        brokers[resourceId] = definition;
        return this;
    }
}

public sealed class LocalRabbitMQDockerDefinition
{
    public string ContainerName { get; set; } = string.Empty;

    public string? UsernameConfigurationKey { get; set; }

    public string? PasswordConfigurationKey { get; set; }

    public string Username { get; set; } =
        RabbitMQResourceDefaults.DefaultUsername;

    public string Password { get; set; } =
        RabbitMQResourceDefaults.DefaultPassword;

    public string? VirtualHostConfigurationKey { get; set; }

    public string? VirtualHost { get; set; }

    public string ContainerImage { get; set; } =
        RabbitMQResourceDefaults.ContainerImage;

    public TimeSpan RemoveTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

public sealed class LocalRabbitMQDockerRuntimeHandler(
    ILocalRabbitMQDockerCommandRunner docker,
    IServiceScopeFactory scopeFactory,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration,
    IOptions<LocalRabbitMQDockerRuntimeOptions> options,
    IRabbitMQBootstrapCredentialProvider bootstrapCredentials) : IRabbitMQRuntimeHandler
{
    private const string RuntimeLifecycleFailedDiagnosticCode =
        "application.rabbitmq.localDockerRuntimeLifecycleFailed";
    private const string RabbitMQDataPath = RabbitMQResourceDefaults.DataPath;
    private readonly LocalRabbitMQDockerRuntimeOptions options = options.Value;
    private readonly Dictionary<string, RabbitMQRuntimeStatus> statusByResourceId =
        new(StringComparer.OrdinalIgnoreCase);

    public RabbitMQRuntimeStatus GetStatus(Resource resource) =>
        TryGetDefinition(resource, out _) &&
        statusByResourceId.TryGetValue(resource.EffectiveResourceId, out var status)
            ? status
            : RabbitMQRuntimeStatus.Unknown;

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
                    statusByResourceId[resource.EffectiveResourceId] = RabbitMQRuntimeStatus.Running;
                    break;
                case ResourceActionIds.Stop:
                    await RemoveAsync(definition, cancellationToken);
                    bootstrapCredentials.Forget(resource.EffectiveResourceId);
                    statusByResourceId[resource.EffectiveResourceId] = RabbitMQRuntimeStatus.Stopped;
                    break;
                case ResourceActionIds.Restart:
                    await RemoveAsync(definition, cancellationToken);
                    bootstrapCredentials.Forget(resource.EffectiveResourceId);
                    await StartAsync(resource, definition, cancellationToken);
                    statusByResourceId[resource.EffectiveResourceId] = RabbitMQRuntimeStatus.Running;
                    break;
                default:
                    throw new NotSupportedException(
                        $"The local RabbitMQ Docker runtime does not support operation '{operationId}'.");
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

    private async Task StartAsync(
        Resource resource,
        LocalRabbitMQDockerDefinition definition,
        CancellationToken cancellationToken)
    {
        var status = await docker.RunAsync(
            ["container", "inspect", "--format", "{{.State.Status}}", definition.ContainerName],
            cancellationToken,
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

        var startup = RabbitMQResourceConfiguration.ResolveStartupConfiguration(
            resource,
            definition,
            configuration,
            bootstrapCredentials);
        var arguments = new List<string>
        {
            "run",
            "-d",
            "--name",
            definition.ContainerName,
        };

        if (startup.Credentials is { } credentials)
        {
            arguments.Add("-e");
            arguments.Add($"RABBITMQ_DEFAULT_USER={credentials.UserName}");
            arguments.Add("-e");
            arguments.Add($"RABBITMQ_DEFAULT_PASS={credentials.Password}");
        }

        if (!string.IsNullOrWhiteSpace(startup.VirtualHost))
        {
            arguments.Add("-e");
            arguments.Add($"RABBITMQ_DEFAULT_VHOST={startup.VirtualHost}");
        }

        arguments.AddRange(
        [
            "-p",
            $"127.0.0.1:{ResolveEndpointPort(resource, "amqp", "tcp").ToString(CultureInfo.InvariantCulture)}:5672"
        ]);

        if (TryResolveEndpointPort(resource, "management", "http", out var managementPort))
        {
            arguments.Add("-p");
            arguments.Add($"127.0.0.1:{managementPort.ToString(CultureInfo.InvariantCulture)}:15672");
        }

        var volumeMount = await TryResolveRabbitMQDataMountAsync(resource, cancellationToken);
        if (volumeMount is not null)
        {
            Directory.CreateDirectory(volumeMount.SourcePath);
            arguments.Add("-v");
            arguments.Add($"{volumeMount.SourcePath}:{volumeMount.TargetPath}{(volumeMount.ReadOnly ? ":ro" : string.Empty)}");
        }

        arguments.Add(definition.ContainerImage);

        await RunRabbitMQContainerAsync(definition, arguments, cancellationToken);
    }

    private async Task RunRabbitMQContainerAsync(
        LocalRabbitMQDockerDefinition definition,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await docker.RunAsync(
            arguments,
            cancellationToken,
            throwOnError: false);
        if (result.ExitCode == 0)
        {
            return;
        }

        await RemoveAsync(definition, cancellationToken);
        if (IsTransientMountSourceFailure(result))
        {
            await Task.Delay(500, cancellationToken);
            result = await docker.RunAsync(
                arguments,
                cancellationToken,
                throwOnError: false);
            if (result.ExitCode == 0)
            {
                return;
            }

            await RemoveAsync(definition, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Docker command 'docker {string.Join(' ', arguments)}' failed with exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}: {result.Error}");
    }

    private static bool IsTransientMountSourceFailure(LocalRabbitMQDockerCommandResult result) =>
        result.Error.Contains("creating mount source path", StringComparison.OrdinalIgnoreCase) &&
        result.Error.Contains("no such file or directory", StringComparison.OrdinalIgnoreCase);

    private async Task<ResolvedRabbitMQVolumeMount?> TryResolveRabbitMQDataMountAsync(
        Resource resource,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var graphModel = scope.ServiceProvider.GetRequiredService<ResourceGraphModel>();
        var graphResolver = scope.ServiceProvider.GetRequiredService<ResourceGraphResolver>();
        var snapshot = await graphModel.GetSnapshotAsync(cancellationToken);
        var graphResolution = graphResolver.ResolveResourceAndDependencies(
            snapshot,
            resource.EffectiveResourceId);
        if (graphResolution.HasErrors)
        {
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                graphResolution.Diagnostics.Select(diagnostic => diagnostic.Message)));
        }

        var rabbitResource = graphResolution.Target ?? resource;
        var volumeConsumer =
            rabbitResource.State.GetCapability<VolumeConsumerDefinition>(
                VolumeConsumerCapabilityProvider.CapabilityIdValue) ??
            rabbitResource.Capabilities.Get<VolumeConsumerDefinition>(
                VolumeConsumerCapabilityProvider.CapabilityIdValue);
        if (volumeConsumer is { Mounts: null })
        {
            throw new InvalidOperationException(
                "The RabbitMQ volume consumer capability is declared but does not include volume mounts.");
        }

        var mount = volumeConsumer?.Mounts?.FirstOrDefault(mount =>
            string.Equals(mount.TargetPath, RabbitMQDataPath, StringComparison.OrdinalIgnoreCase));
        if (mount is null)
        {
            return null;
        }

        var volume = FindResolvedResource(graphResolution.Resources, mount.Volume);
        if (volume.Type.TypeId != CloudShellVolumeResourceTypeProvider.ResourceTypeId)
        {
            throw new InvalidOperationException(
                $"RabbitMQ data volume '{mount.Volume}' must be a '{CloudShellVolumeResourceTypeProvider.ResourceTypeId}' resource.");
        }

        var sourcePath = ResolveVolumeSourcePath(volume, graphResolution.Resources, mount.Volume);

        return new(
            sourcePath,
            mount.TargetPath,
            mount.ReadOnly);
    }

    private static Resource FindResolvedResource(
        IReadOnlyList<Resource> resources,
        string resourceId) =>
        resources.FirstOrDefault(resource =>
            string.Equals(
                resource.EffectiveResourceId,
                resourceId,
                StringComparison.OrdinalIgnoreCase)) ??
        throw new InvalidOperationException(
            $"Resource graph state '{resourceId}' was not resolved.");

    private string ResolveVolumeSourcePath(
        Resource volume,
        IReadOnlyList<Resource> resources,
        string volumeResourceId)
    {
        var directLocation = volume.Attributes.GetString(CloudShellVolumeResourceTypeProvider.Attributes.Location);
        if (!string.IsNullOrWhiteSpace(directLocation))
        {
            return ResolvePath(directLocation, subPath: null);
        }

        var storageResourceId = volume.State.StartupDependencies
            .Select(reference => reference.TryGetResourceId(out var resourceId) ? resourceId : null)
            .FirstOrDefault(resourceId => !string.IsNullOrWhiteSpace(resourceId)) ??
            throw new InvalidOperationException(
                $"RabbitMQ data volume '{volumeResourceId}' must declare a local path or reference a storage resource.");
        var storage = FindResolvedResource(resources, storageResourceId);
        if (storage.Type.TypeId != StorageResourceTypeProvider.ResourceTypeId)
        {
            throw new InvalidOperationException(
                $"RabbitMQ data volume '{volumeResourceId}' references '{storage.Type.TypeId}', expected '{StorageResourceTypeProvider.ResourceTypeId}'.");
        }

        var storageLocation = storage.Attributes.GetString(StorageResourceTypeProvider.Attributes.Location) ??
            throw new InvalidOperationException(
                $"Storage resource '{storage.EffectiveResourceId}' must declare a storage location.");
        var subPath = volume.Attributes.GetString(CloudShellVolumeResourceTypeProvider.Attributes.SubPath);
        return ResolvePath(storageLocation, subPath);
    }

    private async Task RemoveAsync(
        LocalRabbitMQDockerDefinition definition,
        CancellationToken cancellationToken) =>
        await docker.RunAsync(
            ["rm", "-f", definition.ContainerName],
            cancellationToken,
            throwOnError: false,
            commandTimeout: definition.RemoveTimeout);

    private string ResolvePath(
        string storageLocation,
        string? subPath)
    {
        var root = Path.IsPathRooted(storageLocation)
            ? storageLocation
            : Path.Combine(hostEnvironment.ContentRootPath, storageLocation);
        var path = string.IsNullOrWhiteSpace(subPath)
            ? root
            : Path.Combine(root, subPath);
        return Path.GetFullPath(path);
    }

    private static int ResolveEndpointPort(
        Resource resource,
        string endpointName,
        string protocol) =>
        TryResolveEndpointPort(resource, endpointName, protocol, out var port)
            ? port
            : throw new InvalidOperationException(
                $"The RabbitMQ resource must declare a '{endpointName}' endpoint request with a host port before it can be started.");

    private static bool TryResolveEndpointPort(
        Resource resource,
        string endpointName,
        string protocol,
        out int port)
    {
        var endpoint = resource.Attributes
            .GetObject<NetworkingEndpointRequestValue[]>(
                RabbitMQResourceTypeProvider.Attributes.EndpointRequests)?
            .FirstOrDefault(endpoint =>
                string.Equals(endpoint.Name, endpointName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(endpoint.Protocol, protocol, StringComparison.OrdinalIgnoreCase));

        if (endpoint?.Port is > 0)
        {
            port = endpoint.Port.Value;
            return true;
        }

        port = 0;
        return false;
    }

    private bool TryGetDefinition(
        Resource resource,
        out LocalRabbitMQDockerDefinition definition) =>
        LocalRabbitMQDockerRuntimeDefinitions.TryGetDefinition(
            resource.EffectiveResourceId,
            resource.Name,
            resource.Type.TypeId.ToString(),
            options,
            configuration,
            hostEnvironment,
            out definition);

    private sealed record ResolvedRabbitMQVolumeMount(
        string SourcePath,
        string TargetPath,
        bool ReadOnly);
}

internal static class LocalRabbitMQDockerRuntimeDefinitions
{
    public static bool TryGetDefinition(
        string resourceId,
        string resourceName,
        string resourceTypeId,
        LocalRabbitMQDockerRuntimeOptions options,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        out LocalRabbitMQDockerDefinition definition)
    {
        if (options.Brokers.TryGetValue(resourceId, out definition!))
        {
            return true;
        }

        if (!string.Equals(
                resourceTypeId,
                RabbitMQResourceTypeProvider.ResourceTypeId.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        definition = new()
        {
            ContainerName = ResolveDefaultContainerName(resourceName, configuration, hostEnvironment)
        };
        return true;
    }

    private static string ResolveDefaultContainerName(
        string resourceName,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var scope = ResolveRuntimeNameScope(configuration, hostEnvironment);
        var sanitizedResourceName = SanitizeDockerNamePart(resourceName);
        return TruncateDockerName($"cloudshell-{scope}-rabbitmq-{sanitizedResourceName}", maxLength: 63);
    }

    private static string ResolveRuntimeNameScope(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment)
    {
        var configuredScope = configuration["CloudShell:RuntimeNameScope"];
        if (!string.IsNullOrWhiteSpace(configuredScope))
        {
            return SanitizeDockerNamePart(configuredScope);
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hostEnvironment.ContentRootPath));
        return Convert
            .ToHexString(hashBytes, 0, 4)
            .ToLowerInvariant();
    }

    private static string SanitizeDockerNamePart(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) ? character : '-');
        }

        var sanitized = builder
            .ToString()
            .Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "resource" : sanitized;
    }

    private static string TruncateDockerName(string name, int maxLength)
    {
        if (name.Length <= maxLength)
        {
            return name;
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        var suffix = Convert
            .ToHexString(hashBytes, 0, 4)
            .ToLowerInvariant();
        var prefixLength = Math.Max(1, maxLength - suffix.Length - 1);
        return $"{name[..prefixLength].TrimEnd('-')}-{suffix}";
    }
}

public interface ILocalRabbitMQDockerCommandRunner
{
    LocalRabbitMQDockerCommandResult Run(
        IReadOnlyList<string> arguments,
        bool throwOnError = true);

    Task<LocalRabbitMQDockerCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true,
        TimeSpan? commandTimeout = null);
}

public sealed class ProcessLocalRabbitMQDockerCommandRunner(
    IEnumerable<IContainerHostProvider> containerHostProviders) : ILocalRabbitMQDockerCommandRunner
{
    public LocalRabbitMQDockerCommandResult Run(
        IReadOnlyList<string> arguments,
        bool throwOnError = true) =>
        RunAsync(arguments, CancellationToken.None, throwOnError)
            .GetAwaiter()
            .GetResult();

    public async Task<LocalRabbitMQDockerCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true,
        TimeSpan? commandTimeout = null)
    {
        var host = containerHostProviders.FirstOrDefault()?.GetDefaultHost();
        var startInfo = new ProcessStartInfo(ResolveExecutable(host))
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        ConfigureEnvironment(startInfo, host);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var commandTimeoutSource = commandTimeout is { } timeout
            ? new CancellationTokenSource(timeout)
            : null;
        using var linkedCancellationSource = commandTimeoutSource is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                commandTimeoutSource.Token);
        var effectiveCancellationToken = linkedCancellationSource?.Token ?? cancellationToken;

        try
        {
            using var process = Process.Start(startInfo) ??
                throw new InvalidOperationException("Docker command could not be started.");
            using var cancellationRegistration = effectiveCancellationToken.Register(
                static state => KillProcessTree((Process)state!),
                process);
            var outputTask = process.StandardOutput.ReadToEndAsync(effectiveCancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(effectiveCancellationToken);
            await process.WaitForExitAsync(effectiveCancellationToken);
            var result = new LocalRabbitMQDockerCommandResult(
                process.ExitCode,
                await outputTask,
                await errorTask);
            if (throwOnError && result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Docker command '{startInfo.FileName} {string.Join(' ', arguments)}' failed with exit code {result.ExitCode}: {result.Error}");
            }

            return result;
        }
        catch (OperationCanceledException) when (
            commandTimeoutSource?.IsCancellationRequested == true &&
            !cancellationToken.IsCancellationRequested)
        {
            var message = $"Docker command '{startInfo.FileName} {string.Join(' ', arguments)}' timed out after {commandTimeout!.Value.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.";
            if (throwOnError)
            {
                throw new TimeoutException(message);
            }

            return new(
                -1,
                string.Empty,
                message);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if (throwOnError)
            {
                throw;
            }

            return new(
                -1,
                string.Empty,
                exception.Message);
        }
    }

    private static string ResolveExecutable(ContainerHostDescriptor? host) =>
        host?.HostMetadata.TryGetValue("cloudshell.executable", out var executable) == true &&
        !string.IsNullOrWhiteSpace(executable)
            ? executable
            : host?.Kind == ContainerHostKind.Podman ? "podman" : "docker";

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
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void ConfigureEnvironment(
        ProcessStartInfo startInfo,
        ContainerHostDescriptor? host)
    {
        if (host is null ||
            string.IsNullOrWhiteSpace(host.Endpoint))
        {
            return;
        }

        if (host.Kind == ContainerHostKind.Podman)
        {
            startInfo.Environment["CONTAINER_HOST"] = host.Endpoint;
            return;
        }

        startInfo.Environment["DOCKER_HOST"] = host.Endpoint;
    }
}

public sealed record LocalRabbitMQDockerCommandResult(
    int ExitCode,
    string Output,
    string Error);
