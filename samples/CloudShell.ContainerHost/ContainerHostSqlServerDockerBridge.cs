using System.Diagnostics;
using System.Globalization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using Microsoft.Extensions.Hosting;
using ResourceModelResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.ContainerHost;

public sealed class ContainerHostSqlServerDockerBridge(
    IContainerHostDockerCommandRunner docker,
    IServiceScopeFactory scopeFactory,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration) : IContainerHostSqlServerRuntimeBridge
{
    public const string SqlServerContainerName = "cloudshell-container-host-sql-server";
    private const string SqlServerDataPath = SqlServerResourceDefaults.DataPath;
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
                        $"The ContainerHost sample does not map SQL operation '{operationId}' to the Docker SQL runtime.");
            }

            return [];
        }
        catch (Exception exception)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "containerHost.sqlServer.dockerRuntimeLifecycleFailed",
                    exception.Message,
                    resource.EffectiveResourceId)
            ];
        }
    }

    private async Task StartAsync(
        ResourceModelResource resource,
        CancellationToken cancellationToken)
    {
        var status = docker.Run(
            ["container", "inspect", "--format", "{{.State.Status}}", SqlServerContainerName],
            throwOnError: false);
        if (status.ExitCode == 0)
        {
            if (string.Equals(status.Output.Trim(), "running", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await docker.RunAsync(["start", SqlServerContainerName], cancellationToken);
            return;
        }

        var volumeMount = await ResolveSqlDataMountAsync(resource, cancellationToken);
        Directory.CreateDirectory(volumeMount.SourcePath);

        await RunSqlServerContainerAsync(
            [
                "run",
                "-d",
                "--name",
                SqlServerContainerName,
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

    private static bool IsTransientMountSourceFailure(ContainerHostDockerCommandResult result) =>
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
                $"The SQL Server resource must mount a volume at '{SqlServerDataPath}'.");

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
        await docker.RunAsync(["rm", "-f", SqlServerContainerName], cancellationToken, throwOnError: false);

    private string ResolveAdministratorPassword() =>
        configuration["ContainerHost:SqlServer:Password"] ??
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
                "The SQL Server resource must declare a tds endpoint request with a host port before it can be started.");
    }

    private sealed record ResolvedSqlVolumeMount(
        string SourcePath,
        string TargetPath,
        bool ReadOnly);
}

public interface IContainerHostDockerCommandRunner
{
    ContainerHostDockerCommandResult Run(
        IReadOnlyList<string> arguments,
        bool throwOnError = true);

    Task<ContainerHostDockerCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true);
}

public sealed class ProcessContainerHostDockerCommandRunner(
    IEnumerable<IContainerHostProvider> containerHostProviders) : IContainerHostDockerCommandRunner
{
    public ContainerHostDockerCommandResult Run(
        IReadOnlyList<string> arguments,
        bool throwOnError = true) =>
        RunAsync(arguments, CancellationToken.None, throwOnError)
            .GetAwaiter()
            .GetResult();

    public async Task<ContainerHostDockerCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true)
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

        try
        {
            using var process = Process.Start(startInfo) ??
                throw new InvalidOperationException("Docker command could not be started.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var result = new ContainerHostDockerCommandResult(
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
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if (throwOnError)
            {
                throw;
            }

            return new ContainerHostDockerCommandResult(
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

public sealed record ContainerHostDockerCommandResult(
    int ExitCode,
    string Output,
    string Error);
