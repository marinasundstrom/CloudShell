using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceModel;
using CloudShell.ResourceModel.ReferenceProviders;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Hosting;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ApplicationTopologyHost;

public sealed class ApplicationTopologyResourceModelSqlServerDockerBridge(
    IApplicationTopologyDockerCommandRunner docker,
    IServiceScopeFactory scopeFactory,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration,
    IApplicationTopologySqlServerReadinessProbe readinessProbe) : IApplicationTopologyResourceModelSqlServerRuntimeBridge
{
    public const string ResourceModelSqlServerContainerName = "cloudshell-application-topology-sql-server";
    private const string SqlServerDataPath = SqlServerResourceDefaults.DataPath;
    private static readonly TimeSpan DockerRemoveTimeout = TimeSpan.FromSeconds(5);
    private SqlServerRuntimeStatus _status = SqlServerRuntimeStatus.Unknown;

    public SqlServerRuntimeStatus GetStatus(ResourceModelResource resource) => _status;

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        ResourceModelResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            switch (operationId.ToString())
            {
                case ResourceActionIds.Start:
                    await StartAsync(resource, cancellationToken);
                    _status = SqlServerRuntimeStatus.Running;
                    break;
                case ResourceActionIds.Stop:
                    await RemoveAsync(cancellationToken);
                    _status = SqlServerRuntimeStatus.Stopped;
                    break;
                case ResourceActionIds.Restart:
                    await RemoveAsync(cancellationToken);
                    await StartAsync(resource, cancellationToken);
                    _status = SqlServerRuntimeStatus.Running;
                    break;
                default:
                    throw new NotSupportedException(
                        $"The ApplicationTopology sample does not map Resource model SQL operation '{operationId}' to the Docker SQL runtime.");
            }

            return [];
        }
        catch (Exception exception)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "applicationTopology.sqlServer.dockerRuntimeLifecycleFailed",
                    exception.Message,
                    resource.EffectiveResourceId)
            ];
        }
    }

    private async Task StartAsync(
        ResourceModelResource resource,
        CancellationToken cancellationToken)
    {
        var status = await docker.RunAsync(
            ["container", "inspect", "--format", "{{.State.Status}}", ResourceModelSqlServerContainerName],
            cancellationToken,
            throwOnError: false);
        if (status.ExitCode == 0)
        {
            if (string.Equals(status.Output.Trim(), "running", StringComparison.OrdinalIgnoreCase))
            {
                await readinessProbe.WaitUntilReadyAsync(resource, cancellationToken);
                return;
            }

            await docker.RunAsync(["start", ResourceModelSqlServerContainerName], cancellationToken);
            await readinessProbe.WaitUntilReadyAsync(resource, cancellationToken);
            return;
        }

        var volumeMount = await ResolveSqlDataMountAsync(resource, cancellationToken);
        Directory.CreateDirectory(volumeMount.SourcePath);

        await RunSqlServerContainerAsync(
            [
                "run",
                "-d",
                "--name",
                ResourceModelSqlServerContainerName,
                "-e",
                "ACCEPT_EULA=Y",
                "-e",
                $"MSSQL_SA_PASSWORD={ResolveAdministratorPassword()}",
                "-p",
                $"127.0.0.1:{ResolveTdsPort(resource).ToString(CultureInfo.InvariantCulture)}:1433",
                "-v",
                $"{volumeMount.SourcePath}:{volumeMount.TargetPath}{(volumeMount.ReadOnly ? ":ro" : string.Empty)}",
                SqlServerResourceDefaults.ContainerImage
            ],
            cancellationToken);

        await readinessProbe.WaitUntilReadyAsync(resource, cancellationToken);
    }

    private async Task RunSqlServerContainerAsync(
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

        await RemoveAsync(cancellationToken);
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

            await RemoveAsync(cancellationToken);
        }

        throw new InvalidOperationException(
            $"Docker command 'docker {string.Join(' ', arguments)}' failed with exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}: {result.Error}");
    }

    private static bool IsTransientMountSourceFailure(ApplicationTopologyDockerCommandResult result) =>
        result.Error.Contains("creating mount source path", StringComparison.OrdinalIgnoreCase) &&
        result.Error.Contains("no such file or directory", StringComparison.OrdinalIgnoreCase);

    private async Task<ResolvedSqlVolumeMount> ResolveSqlDataMountAsync(
        ResourceModelResource resource,
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

        var sqlResource = graphResolution.Target ?? resource;
        var volumeConsumer = sqlResource.Capabilities.Get<VolumeConsumerDefinition>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue);
        var mount = volumeConsumer?.Mounts.FirstOrDefault(mount =>
            string.Equals(mount.TargetPath, SqlServerDataPath, StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidOperationException(
                $"The Resource model SQL Server resource must mount a volume at '{SqlServerDataPath}'.");

        var volume = FindResolvedResource(graphResolution.Resources, mount.Volume);
        if (volume.Type.TypeId != CloudShellVolumeResourceTypeProvider.ResourceTypeId)
        {
            throw new InvalidOperationException(
                $"SQL Server data volume '{mount.Volume}' must be a '{CloudShellVolumeResourceTypeProvider.ResourceTypeId}' resource.");
        }

        var sourcePath = ResolveVolumeSourcePath(volume, graphResolution.Resources, mount.Volume);

        return new ResolvedSqlVolumeMount(
            sourcePath,
            mount.TargetPath,
            mount.ReadOnly);
    }

    private static ResourceModelResource FindResolvedResource(
        IReadOnlyList<ResourceModelResource> resources,
        string resourceId) =>
        resources.FirstOrDefault(resource =>
            string.Equals(
                resource.EffectiveResourceId,
                resourceId,
                StringComparison.OrdinalIgnoreCase)) ??
        throw new InvalidOperationException(
            $"Resource graph state '{resourceId}' was not resolved.");

    private string ResolveVolumeSourcePath(
        ResourceModelResource volume,
        IReadOnlyList<ResourceModelResource> resources,
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
                $"SQL Server data volume '{volumeResourceId}' must declare a local path or reference a storage resource.");
        var storage = FindResolvedResource(resources, storageResourceId);
        if (storage.Type.TypeId != StorageResourceTypeProvider.ResourceTypeId)
        {
            throw new InvalidOperationException(
                $"SQL Server data volume '{volumeResourceId}' references '{storage.Type.TypeId}', expected '{StorageResourceTypeProvider.ResourceTypeId}'.");
        }

        var storageLocation = storage.Attributes.GetString(StorageResourceTypeProvider.Attributes.Location) ??
            throw new InvalidOperationException(
                $"Storage resource '{storage.EffectiveResourceId}' must declare a storage location.");
        var subPath = volume.Attributes.GetString(CloudShellVolumeResourceTypeProvider.Attributes.SubPath);
        return ResolvePath(storageLocation, subPath);
    }

    private async Task RemoveAsync(CancellationToken cancellationToken) =>
        await docker.RunAsync(
            ["rm", "-f", ResourceModelSqlServerContainerName],
            cancellationToken,
            throwOnError: false,
            commandTimeout: DockerRemoveTimeout);

    private string ResolveAdministratorPassword() =>
        configuration["ApplicationTopology:SqlServer:Password"] ??
        SqlServerResourceDefaults.AdministratorPassword;

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

    private static int ResolveTdsPort(ResourceModelResource resource)
    {
        var endpoint = resource.Attributes
            .GetObject<NetworkingEndpointRequestValue[]>(
                SqlServerResourceTypeProvider.Attributes.EndpointRequests)?
            .FirstOrDefault(endpoint =>
                string.Equals(endpoint.Name, "tds", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(endpoint.Protocol, "tcp", StringComparison.OrdinalIgnoreCase));

        return endpoint?.Port is > 0
            ? endpoint.Port.Value
            : throw new InvalidOperationException(
                "The Resource model SQL Server resource must declare a tds endpoint request with a host port before it can be started.");
    }

    private sealed record ResolvedSqlVolumeMount(
        string SourcePath,
        string TargetPath,
        bool ReadOnly);
}

public interface IApplicationTopologyDockerCommandRunner
{
    ApplicationTopologyDockerCommandResult Run(
        IReadOnlyList<string> arguments,
        bool throwOnError = true);

    Task<ApplicationTopologyDockerCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true,
        TimeSpan? commandTimeout = null);
}

public sealed class ProcessApplicationTopologyDockerCommandRunner(
    IEnumerable<IContainerHostProvider> containerHostProviders) : IApplicationTopologyDockerCommandRunner
{
    public ApplicationTopologyDockerCommandResult Run(
        IReadOnlyList<string> arguments,
        bool throwOnError = true) =>
        RunAsync(arguments, CancellationToken.None, throwOnError)
            .GetAwaiter()
            .GetResult();

    public async Task<ApplicationTopologyDockerCommandResult> RunAsync(
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
            var result = new ApplicationTopologyDockerCommandResult(
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

            return new ApplicationTopologyDockerCommandResult(
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

            return new ApplicationTopologyDockerCommandResult(
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

public sealed record ApplicationTopologyDockerCommandResult(
    int ExitCode,
    string Output,
    string Error);

public interface IApplicationTopologySqlServerReadinessProbe
{
    Task WaitUntilReadyAsync(
        ResourceModelResource resource,
        CancellationToken cancellationToken);
}

public sealed class ApplicationTopologySqlServerReadinessProbe(
    IConfiguration configuration) : IApplicationTopologySqlServerReadinessProbe
{
    public async Task WaitUntilReadyAsync(
        ResourceModelResource resource,
        CancellationToken cancellationToken)
    {
        if (!ResourceModelSqlServerConnectionSupport.TryCreateAdministratorConnectionString(
                resource,
                configuration,
                "master",
                out var connectionString))
        {
            throw new InvalidOperationException(
                $"SQL Server resource '{resource.Name}' cannot be started because its TDS endpoint or administrator password is not available.");
        }

        await using var connection = await ResourceModelSqlServerConnectionSupport.OpenWithRetryAsync(
            resource,
            connectionString,
            cancellationToken);
    }
}
