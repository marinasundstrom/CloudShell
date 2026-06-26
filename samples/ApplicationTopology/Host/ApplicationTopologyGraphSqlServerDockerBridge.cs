using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using System.Diagnostics;
using System.Globalization;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.ApplicationTopologyHost;

public sealed class ApplicationTopologyGraphSqlServerDockerBridge(
    IApplicationTopologyDockerCommandRunner docker,
    IConfiguration configuration) : IApplicationTopologyGraphSqlServerRuntimeBridge
{
    public const string GraphSqlServerContainerName = "cloudshell-application-topology-graph-sql-server";
    private SqlServerRuntimeStatus _status = SqlServerRuntimeStatus.Unknown;

    public SqlServerRuntimeStatus GetStatus(GraphResource resource) => _status;

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
                        $"The ApplicationTopology sample does not map graph SQL operation '{operationId}' to the Docker SQL runtime.");
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
        GraphResource resource,
        CancellationToken cancellationToken)
    {
        var status = docker.Run(
            ["container", "inspect", "--format", "{{.State.Status}}", GraphSqlServerContainerName],
            throwOnError: false);
        if (status.ExitCode == 0)
        {
            if (string.Equals(status.Output.Trim(), "running", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await docker.RunAsync(["start", GraphSqlServerContainerName], cancellationToken);
            return;
        }

        await docker.RunAsync(
            [
                "run",
                "-d",
                "--name",
                GraphSqlServerContainerName,
                "-e",
                "ACCEPT_EULA=Y",
                "-e",
                $"MSSQL_SA_PASSWORD={ResolveAdministratorPassword()}",
                "-p",
                $"127.0.0.1:{ResolveTdsPort(resource).ToString(CultureInfo.InvariantCulture)}:1433",
                ApplicationProviderServiceCollectionExtensions.DefaultSqlServerImage
            ],
            cancellationToken);
    }

    private async Task RemoveAsync(CancellationToken cancellationToken) =>
        await docker.RunAsync(["rm", "-f", GraphSqlServerContainerName], cancellationToken, throwOnError: false);

    private string ResolveAdministratorPassword() =>
        configuration["ApplicationTopology:SqlServer:Password"] ??
        ApplicationProviderServiceCollectionExtensions.DefaultSqlServerAdministratorPassword;

    private static int ResolveTdsPort(GraphResource resource)
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
                "The graph SQL Server resource must declare a tds endpoint request with a host port before it can be started.");
    }
}

public interface IApplicationTopologyDockerCommandRunner
{
    ApplicationTopologyDockerCommandResult Run(
        IReadOnlyList<string> arguments,
        bool throwOnError = true);

    Task<ApplicationTopologyDockerCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true);
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
