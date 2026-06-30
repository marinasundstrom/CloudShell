using Microsoft.Extensions.Hosting;

namespace CloudShell.ControlPlane.Providers;

public sealed class LocalDockerContainerApplicationRuntimeOptions
{
    private readonly Dictionary<string, LocalDockerContainerApplicationRuntimeDefinition> applications =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, LocalDockerContainerApplicationRuntimeDefinition> Applications => applications;

    public LocalDockerContainerApplicationRuntimeOptions AddApplication(
        string resourceId,
        string projectPath,
        Action<LocalDockerContainerApplicationRuntimeDefinition>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        var definition = new LocalDockerContainerApplicationRuntimeDefinition(resourceId, projectPath);
        configure?.Invoke(definition);
        applications[resourceId] = definition;
        return this;
    }
}

public sealed class LocalDockerContainerApplicationRuntimeDefinition(
    string resourceId,
    string projectPath)
{
    public string ResourceId { get; } = resourceId;

    public string ProjectPath { get; set; } = projectPath;

    public string ContainerNetworkName { get; set; } = "cloudshell";

    public string IngressImage { get; set; } = "traefik:v3.0";

    public string IngressContainerName { get; set; } = "cloudshell-container-app-ingress";

    public string IngressConfigurationDirectory { get; set; } =
        Path.Combine("Data", "runtime-ingress");

    public string ReplicaContainerNamePrefix { get; set; } = "cloudshell-container-app-replica-";

    public string ReplicaNetworkAliasPrefix { get; set; } = "cloudshell-container-app-replica-";

    public string ReplicaResourceIdPrefix { get; set; } = "runtime-container:container-app:replica-";

    public string RuntimeResourceProviderId { get; set; } = "local-docker-container-application.runtime";

    public string RuntimeResourceProviderName { get; set; } = "Local Docker container application runtime";

    public string RuntimeMaterialization { get; set; } = "localDockerContainerApplication";

    public string ReplicaServiceNamePrefix { get; set; } = "container-app-replica-";

    public string? TraceIngestEndpoint { get; set; }

    public string? MetricIngestEndpoint { get; set; }

    public int? ReplicaProbePortStart { get; set; }

    public int? ReplicaCleanupLimit { get; set; }

    public TimeSpan? StatusProbeTimeout { get; set; }

    public TimeSpan? StatusCacheDuration { get; set; }

    public string ResolveProjectPath(IHostEnvironment? hostEnvironment) =>
        ResolvePath(hostEnvironment, ProjectPath);

    public string ResolveIngressConfigurationDirectory(IHostEnvironment? hostEnvironment) =>
        ResolvePath(hostEnvironment, IngressConfigurationDirectory);

    private static string ResolvePath(
        IHostEnvironment? hostEnvironment,
        string path) =>
        Path.IsPathRooted(path) || hostEnvironment is null
            ? path
            : Path.Combine(hostEnvironment.ContentRootPath, path);
}
