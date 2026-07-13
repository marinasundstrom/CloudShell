using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;

namespace CloudShell.ControlPlane.Providers;

public sealed class LocalSqlServerDockerRuntimeOptions
{
    private readonly Dictionary<string, LocalSqlServerDockerDefinition> servers =
        new(StringComparer.OrdinalIgnoreCase);

    public IDictionary<string, LocalSqlServerDockerDefinition> Servers => servers;

    public LocalSqlServerDockerRuntimeOptions AddServer(
        string resourceId,
        string containerName,
        Action<LocalSqlServerDockerDefinition>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);

        var definition = new LocalSqlServerDockerDefinition
        {
            ContainerName = containerName
        };
        configure?.Invoke(definition);
        servers[resourceId] = definition;
        return this;
    }
}

public sealed class LocalSqlServerDockerDefinition
{
    public string ContainerName { get; set; } = string.Empty;

    public string? PasswordConfigurationKey { get; set; }

    public string AdministratorPassword { get; set; } =
        SqlServerResourceDefaults.AdministratorPassword;

    public string ContainerImage { get; set; } =
        SqlServerResourceDefaults.ContainerImage;

    public bool WaitUntilReady { get; set; }

    public TimeSpan RemoveTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

public sealed class LocalSqlServerDockerRuntimeHandler(
    ILocalSqlServerDockerCommandRunner docker,
    IServiceScopeFactory scopeFactory,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration,
    ILocalSqlServerReadinessProbe readinessProbe,
    IOptions<LocalSqlServerDockerRuntimeOptions> options) : ISqlServerRuntimeHandler
{
    private const string RuntimeLifecycleFailedDiagnosticCode =
        "application.sqlServer.localDockerRuntimeLifecycleFailed";
    private const string SqlServerDataPath = SqlServerResourceDefaults.DataPath;
    private readonly LocalSqlServerDockerRuntimeOptions options = options.Value;
    private readonly Dictionary<string, SqlServerRuntimeStatus> statusByResourceId =
        new(StringComparer.OrdinalIgnoreCase);

    public SqlServerRuntimeStatus GetStatus(Resource resource) =>
        TryGetDefinition(resource, out _) &&
        statusByResourceId.TryGetValue(resource.EffectiveResourceId, out var status)
            ? status
            : SqlServerRuntimeStatus.Unknown;

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
                    statusByResourceId[resource.EffectiveResourceId] = SqlServerRuntimeStatus.Running;
                    break;
                case ResourceActionIds.Stop:
                    await RemoveAsync(definition, cancellationToken);
                    statusByResourceId[resource.EffectiveResourceId] = SqlServerRuntimeStatus.Stopped;
                    break;
                case ResourceActionIds.Restart:
                    await RemoveAsync(definition, cancellationToken);
                    await StartAsync(resource, definition, cancellationToken);
                    statusByResourceId[resource.EffectiveResourceId] = SqlServerRuntimeStatus.Running;
                    break;
                default:
                    throw new NotSupportedException(
                        $"The local SQL Server Docker runtime does not support operation '{operationId}'.");
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
        LocalSqlServerDockerDefinition definition,
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
                await WaitUntilReadyAsync(resource, definition, cancellationToken);
                return;
            }

            await docker.RunAsync(["start", definition.ContainerName], cancellationToken);
            await WaitUntilReadyAsync(resource, definition, cancellationToken);
            return;
        }

        var volumeMount = await ResolveSqlDataMountAsync(resource, cancellationToken);
        Directory.CreateDirectory(volumeMount.SourcePath);

        await RunSqlServerContainerAsync(
            definition,
            [
                "run",
                "-d",
                "--name",
                definition.ContainerName,
                "-e",
                "ACCEPT_EULA=Y",
                "-e",
                $"MSSQL_SA_PASSWORD={ResolveAdministratorPassword(definition)}",
                "-p",
                $"127.0.0.1:{ResolveTdsPort(resource).ToString(CultureInfo.InvariantCulture)}:1433",
                "-v",
                $"{volumeMount.SourcePath}:{volumeMount.TargetPath}{(volumeMount.ReadOnly ? ":ro" : string.Empty)}",
                definition.ContainerImage
            ],
            cancellationToken);

        await WaitUntilReadyAsync(resource, definition, cancellationToken);
    }

    private async Task RunSqlServerContainerAsync(
        LocalSqlServerDockerDefinition definition,
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

    private static bool IsTransientMountSourceFailure(LocalSqlServerDockerCommandResult result) =>
        result.Error.Contains("creating mount source path", StringComparison.OrdinalIgnoreCase) &&
        result.Error.Contains("no such file or directory", StringComparison.OrdinalIgnoreCase);

    private async Task<ResolvedSqlVolumeMount> ResolveSqlDataMountAsync(
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

    private async Task RemoveAsync(
        LocalSqlServerDockerDefinition definition,
        CancellationToken cancellationToken) =>
        await docker.RunAsync(
            ["rm", "-f", definition.ContainerName],
            cancellationToken,
            throwOnError: false,
            commandTimeout: definition.RemoveTimeout);

    private string ResolveAdministratorPassword(LocalSqlServerDockerDefinition definition) =>
        definition.PasswordConfigurationKey is { Length: > 0 } key
            ? configuration[key] ?? definition.AdministratorPassword
            : definition.AdministratorPassword;

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

    private async Task WaitUntilReadyAsync(
        Resource resource,
        LocalSqlServerDockerDefinition definition,
        CancellationToken cancellationToken)
    {
        if (!definition.WaitUntilReady)
        {
            return;
        }

        await readinessProbe.WaitUntilReadyAsync(resource, cancellationToken);
    }

    private static int ResolveTdsPort(Resource resource)
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

    private bool TryGetDefinition(
        Resource resource,
        out LocalSqlServerDockerDefinition definition) =>
        options.Servers.TryGetValue(resource.EffectiveResourceId, out definition!);

    private sealed record ResolvedSqlVolumeMount(
        string SourcePath,
        string TargetPath,
        bool ReadOnly);
}

public interface ILocalSqlServerDockerCommandRunner
{
    LocalSqlServerDockerCommandResult Run(
        IReadOnlyList<string> arguments,
        bool throwOnError = true);

    Task<LocalSqlServerDockerCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true,
        TimeSpan? commandTimeout = null);
}

public sealed class ProcessLocalSqlServerDockerCommandRunner(
    IContainerHostCommandPlatform commandPlatform) : ILocalSqlServerDockerCommandRunner
{
    public LocalSqlServerDockerCommandResult Run(
        IReadOnlyList<string> arguments,
        bool throwOnError = true) =>
        RunAsync(arguments, CancellationToken.None, throwOnError)
            .GetAwaiter()
            .GetResult();

    public async Task<LocalSqlServerDockerCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true,
        TimeSpan? commandTimeout = null)
    {
        var plan = commandPlatform.CreatePlan();
        if (!plan.IsAvailable)
        {
            if (throwOnError)
            {
                throw new InvalidOperationException(plan.UnavailableReason);
            }

            return new(
                -1,
                string.Empty,
                plan.UnavailableReason ?? "Container runtime command is unavailable.");
        }

        var startInfo = plan.CreateStartInfo(arguments);

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
            var result = new LocalSqlServerDockerCommandResult(
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

}

public sealed record LocalSqlServerDockerCommandResult(
    int ExitCode,
    string Output,
    string Error);

public interface ILocalSqlServerReadinessProbe
{
    Task WaitUntilReadyAsync(
        Resource resource,
        CancellationToken cancellationToken);
}

public sealed class NoopLocalSqlServerReadinessProbe : ILocalSqlServerReadinessProbe
{
    public Task WaitUntilReadyAsync(
        Resource resource,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
