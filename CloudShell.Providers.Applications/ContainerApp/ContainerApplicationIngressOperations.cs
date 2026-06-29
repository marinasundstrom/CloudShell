using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CloudShell.Providers.Applications;

internal sealed partial class ContainerApplicationIngressOperations(
    ApplicationProviderOptions options,
    IHostEnvironment environment,
    ILoggerFactory? loggerFactory = null)
{
    private const string DefaultContainerNetworkName = "cloudshell";

    private readonly ApplicationResourcePortResolver _ports = new(options);
    private readonly ILogger _dockerHostLogger =
        loggerFactory?.CreateLogger(CloudShellLogCategories.DockerHostLifecycle) ??
        NullLogger.Instance;

    public bool ShouldUseIngress(ResourceOrchestratorService service) =>
        options.EnableReplicatedContainerAppIngress &&
        service.ReplicasEnabled &&
        service.ServicePorts.Any(IsIngressPort);

    public async Task StartAsync(
        ApplicationResourceDefinition definition,
        ContainerHostDescriptor engine,
        ResourceOrchestratorService service,
        ApplicationProcessLog log,
        string providerId,
        CancellationToken cancellationToken,
        ResourceProcedureContext? procedureContext = null)
    {
        var ingressPorts = service.ServicePorts
            .Where(IsIngressPort)
            .ToArray();
        if (ingressPorts.Length == 0)
        {
            return;
        }

        var ingressName = CreateIngressName(service);
        var configurationDirectory = await WriteConfigurationAsync(
            definition,
            service,
            cancellationToken,
            providerId,
            procedureContext);

        var runningStatus = await TryGetContainerRunningStatusAsync(
            engine,
            ingressName,
            cancellationToken);
        if (runningStatus == true)
        {
            log.Append(
                $"Updated replicated container app ingress mapping '{ingressName}' for {definition.Name}.",
                "process",
                "Information");
            procedureContext?.AppendProviderEvent(
                providerId,
                "application.container.ingress.updated",
                $"Application provider updated ingress '{ingressName}' for '{definition.Name}'.");
            return;
        }

        if (runningStatus == false)
        {
            procedureContext?.AppendProviderEvent(
                providerId,
                "application.container.ingress.starting",
                $"Application provider is starting existing ingress '{ingressName}' for '{definition.Name}'.");
            await ApplicationContainerHostCommands.RunAsync(
                engine,
                ["start", ingressName],
                log,
                cancellationToken,
                _dockerHostLogger);
            log.Append(
                $"Started existing replicated container app ingress '{ingressName}' for {definition.Name}.",
                "process",
                "Information");
            procedureContext?.AppendProviderEvent(
                providerId,
                "application.container.ingress.started",
                $"Application provider started existing ingress '{ingressName}' for '{definition.Name}'.");
            return;
        }

        var arguments = new List<string>
        {
            "run",
            "-d",
            "--name",
            ingressName,
            "--network",
            DefaultContainerNetworkName
        };

        foreach (var port in ingressPorts)
        {
            var hostPort = _ports.ResolveLocalPort(definition.Id, port);
            arguments.Add("-p");
            arguments.Add($"{hostPort.ToString(CultureInfo.InvariantCulture)}:{hostPort.ToString(CultureInfo.InvariantCulture)}/{ContainerApplicationContainerRunCommandFactory.NormalizeContainerPublishProtocol(port.Protocol)}");
        }

        arguments.Add("-v");
        arguments.Add($"{configurationDirectory}:/etc/traefik/dynamic:ro");
        arguments.Add(options.ReplicatedContainerAppIngressImage);
        arguments.Add("--providers.file.directory=/etc/traefik/dynamic");
        arguments.Add("--providers.file.watch=true");

        foreach (var port in ingressPorts)
        {
            var entrypoint = CreateIngressEntrypoint(port);
            var hostPort = _ports.ResolveLocalPort(definition.Id, port);
            arguments.Add($"--entrypoints.{entrypoint}.address=:{hostPort.ToString(CultureInfo.InvariantCulture)}");
        }

        procedureContext?.AppendProviderEvent(
            providerId,
            "application.container.ingress.starting",
            $"Application provider is starting ingress '{ingressName}' for '{definition.Name}'.");
        await ApplicationContainerHostCommands.RunAsync(
            engine,
            arguments,
            log,
            cancellationToken,
            _dockerHostLogger);
        log.Append(
            $"Started replicated container app ingress '{ingressName}' for {definition.Name}.",
            "process",
            "Information");
        procedureContext?.AppendProviderEvent(
            providerId,
            "application.container.ingress.started",
            $"Application provider started ingress '{ingressName}' for '{definition.Name}'.");
    }

    public async Task StopAsync(
        ApplicationResourceDefinition definition,
        ResourceOrchestratorService service,
        ContainerHostDescriptor engine,
        ApplicationProcessLog log,
        string providerId,
        CancellationToken cancellationToken,
        ResourceProcedureContext? procedureContext = null)
    {
        var ingressName = CreateIngressName(service);
        procedureContext?.AppendProviderEvent(
            providerId,
            "application.container.ingress.stopping",
            $"Application provider is stopping ingress '{ingressName}' for '{definition.Name}'.");
        await StopAsync(
            service,
            engine,
            log,
            cancellationToken,
            _dockerHostLogger);
        procedureContext?.AppendProviderEvent(
            providerId,
            "application.container.ingress.stopped",
            $"Application provider stopped ingress '{ingressName}' for '{definition.Name}'.");
    }

    public static async Task StopAsync(
        ResourceOrchestratorService service,
        ContainerHostDescriptor engine,
        ApplicationProcessLog log,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        await ApplicationContainerHostCommands.RunAsync(
            engine,
            ["rm", "-f", CreateIngressName(service)],
            log,
            cancellationToken,
            logger);
    }

    public static string CreateIngressConfiguration(
        ResourceOrchestratorService service,
        IReadOnlyList<ServicePort> ports)
    {
        var httpPorts = ports
            .Where(port => NormalizeProtocol(port.Protocol) == "http")
            .ToArray();
        var tcpPorts = ports
            .Where(port => NormalizeProtocol(port.Protocol) == "tcp")
            .ToArray();
        var builder = new StringBuilder();

        if (httpPorts.Length > 0)
        {
            builder.AppendLine("http:");
            builder.AppendLine("  routers:");
            foreach (var port in httpPorts)
            {
                var routeId = CreateIngressRouteId(service, port);
                builder.AppendLine(CultureInfo.InvariantCulture, $"    {routeId}:");
                builder.AppendLine(CultureInfo.InvariantCulture, $"      entryPoints: [\"{CreateIngressEntrypoint(port)}\"]");
                builder.AppendLine("      rule: \"PathPrefix(`/`)\"");
                builder.AppendLine(CultureInfo.InvariantCulture, $"      service: \"{routeId}\"");
            }

            builder.AppendLine("  services:");
            foreach (var port in httpPorts)
            {
                var routeId = CreateIngressRouteId(service, port);
                builder.AppendLine(CultureInfo.InvariantCulture, $"    {routeId}:");
                builder.AppendLine("      loadBalancer:");
                builder.AppendLine("        servers:");
                var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);
                foreach (var instance in replicaGroup.Instances)
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"          - url: \"http://{CreateIngressTargetName(service, instance)}:{port.TargetPort.ToString(CultureInfo.InvariantCulture)}\"");
                }
            }
        }

        if (tcpPorts.Length > 0)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine("tcp:");
            builder.AppendLine("  routers:");
            foreach (var port in tcpPorts)
            {
                var routeId = CreateIngressRouteId(service, port);
                builder.AppendLine(CultureInfo.InvariantCulture, $"    {routeId}:");
                builder.AppendLine(CultureInfo.InvariantCulture, $"      entryPoints: [\"{CreateIngressEntrypoint(port)}\"]");
                builder.AppendLine("      rule: \"HostSNI(`*`)\"");
                builder.AppendLine(CultureInfo.InvariantCulture, $"      service: \"{routeId}\"");
            }

            builder.AppendLine("  services:");
            foreach (var port in tcpPorts)
            {
                var routeId = CreateIngressRouteId(service, port);
                builder.AppendLine(CultureInfo.InvariantCulture, $"    {routeId}:");
                builder.AppendLine("      loadBalancer:");
                builder.AppendLine("        servers:");
                var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);
                foreach (var instance in replicaGroup.Instances)
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"          - address: \"{CreateIngressTargetName(service, instance)}:{port.TargetPort.ToString(CultureInfo.InvariantCulture)}\"");
                }
            }
        }

        return builder.ToString();
    }

    public static bool IsIngressPort(ServicePort port) =>
        NormalizeProtocol(port.Protocol) is "http" or "tcp";

    public static string CreateIngressName(ResourceOrchestratorService service) =>
        $"{service.Name}-ingress";

    public static string CreateIngressTargetName(
        ResourceOrchestratorService service,
        ResourceOrchestratorServiceInstance instance) =>
        ApplicationResourceNames.CreateRuntimeNetworkAlias(
            service.Name,
            instance.Name,
            instance.ReplicaOrdinal);

    private async Task<string> WriteConfigurationAsync(
        ApplicationResourceDefinition definition,
        ResourceOrchestratorService service,
        CancellationToken cancellationToken,
        string providerId,
        ResourceProcedureContext? procedureContext = null)
    {
        var ingressPorts = service.ServicePorts
            .Where(IsIngressPort)
            .ToArray();
        var configurationDirectory = GetConfigurationDirectory(definition.Id);
        Directory.CreateDirectory(configurationDirectory);
        var configurationPath = Path.Combine(configurationDirectory, "dynamic.yml");
        procedureContext?.AppendProviderEvent(
            providerId,
            "application.container.ingress.configuring",
            $"Application provider is writing ingress configuration for '{definition.Name}'.");
        await File.WriteAllTextAsync(
            configurationPath,
            CreateIngressConfiguration(service, ingressPorts),
            cancellationToken);

        return configurationDirectory;
    }

    private string GetConfigurationDirectory(string resourceId)
    {
        var root = Path.IsPathRooted(options.IngressConfigurationDirectory)
            ? options.IngressConfigurationDirectory
            : Path.GetFullPath(options.IngressConfigurationDirectory, environment.ContentRootPath);
        var directoryName = SlugPattern()
            .Replace(resourceId.ToLowerInvariant(), "-")
            .Trim('-');

        return Path.Combine(root, string.IsNullOrWhiteSpace(directoryName) ? "container-app" : directoryName);
    }

    private static async Task<bool?> TryGetContainerRunningStatusAsync(
        ContainerHostDescriptor engine,
        string containerName,
        CancellationToken cancellationToken)
    {
        var result = await ApplicationContainerHostCommands.CaptureAsync(
            engine,
            ["inspect", "--format", "{{.State.Running}}", containerName],
            cancellationToken);
        if (result.ExitCode != 0)
        {
            return null;
        }

        var output = result.Output.Trim();
        return bool.TryParse(output, out var running) ? running : null;
    }

    private static string CreateIngressEntrypoint(ServicePort port) =>
        CreateStableIdentifier(string.IsNullOrWhiteSpace(port.Name) ? $"port-{port.TargetPort}" : port.Name);

    private static string CreateIngressRouteId(
        ResourceOrchestratorService service,
        ServicePort port) =>
        CreateStableIdentifier($"{service.Name}-{port.Name}-{port.TargetPort.ToString(CultureInfo.InvariantCulture)}");

    private static string NormalizeProtocol(string? protocol) =>
        string.IsNullOrWhiteSpace(protocol) ? "tcp" : protocol.Trim().ToLowerInvariant();

    private static string CreateStableIdentifier(string value)
        => ApplicationResourceNames.CreateStableIdentifier(value);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugPattern();
}
