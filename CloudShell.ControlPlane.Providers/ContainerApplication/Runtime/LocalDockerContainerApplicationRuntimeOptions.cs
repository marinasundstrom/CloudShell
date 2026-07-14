using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Hosting;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CloudShell.ControlPlane.Providers;

public sealed class LocalDockerContainerApplicationRuntimeOptions
{
    private readonly Dictionary<string, LocalDockerContainerApplicationRuntimeDefinition> applications =
        new(StringComparer.OrdinalIgnoreCase);

    public string? NameScope { get; set; }

    public IReadOnlyDictionary<string, LocalDockerContainerApplicationRuntimeDefinition> Applications => applications;

    public LocalDockerContainerApplicationRuntimeOptions AddApplication(
        string resourceId,
        string projectPath,
        Action<LocalDockerContainerApplicationRuntimeDefinition>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        var definition = LocalDockerContainerApplicationRuntimeDefinition.CreateDefault(resourceId, projectPath, NameScope);
        configure?.Invoke(definition);
        applications[resourceId] = definition;
        return this;
    }

    public bool TryGetApplication(
        string resourceId,
        out LocalDockerContainerApplicationRuntimeDefinition definition) =>
        applications.TryGetValue(resourceId, out definition!);
}

public sealed class LocalDockerContainerApplicationRuntimeDefinition(
    string resourceId,
    string projectPath = "")
{
    private const int DockerServiceNameMaxLength = 48;
    private const int RuntimeNameScopeMaxLength = 16;

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

    public string? RuntimeNameScope { get; set; }

    public string? TraceIngestEndpoint { get; set; }

    public string? MetricIngestEndpoint { get; set; }

    public int? ReplicaProbePortStart { get; set; }

    public int? ReplicaCleanupLimit { get; set; }

    public TimeSpan? StatusProbeTimeout { get; set; }

    public TimeSpan? StatusCacheDuration { get; set; }

    public TimeSpan? MaterializationCommandTimeout { get; set; } =
        TimeSpan.FromMinutes(8);

    public string ContainerPublishOperatingSystem { get; set; } = "linux";

    public string ContainerPublishArchitecture { get; set; } =
        ResolveCurrentContainerPublishArchitecture();

    public string ResolveProjectPath(IHostEnvironment? hostEnvironment) =>
        ResolvePath(hostEnvironment, ProjectPath);

    public string ResolveIngressConfigurationDirectory(IHostEnvironment? hostEnvironment) =>
        ResolvePath(hostEnvironment, IngressConfigurationDirectory);

    public static LocalDockerContainerApplicationRuntimeDefinition CreateDefault(
        string resourceId,
        string? projectPath = null,
        string? nameScope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var serviceName = ResourceOrchestratorReplicaGroups.CreateDefaultServiceName(resourceId);
        var normalizedScope = CreateNameSegment(nameScope);
        var dockerServiceName = CreateScopedServiceName(serviceName, normalizedScope);
        var resourceSegment = CreateResourceSegment(resourceId);
        return new(resourceId, projectPath ?? string.Empty)
        {
            IngressContainerName = $"{dockerServiceName}-ingress",
            IngressConfigurationDirectory = normalizedScope is null
                ? Path.Combine("Data", "runtime-ingress", serviceName)
                : Path.Combine("Data", "runtime-ingress", normalizedScope, serviceName),
            ReplicaContainerNamePrefix = $"{dockerServiceName}-replica-",
            ReplicaNetworkAliasPrefix = $"{dockerServiceName}-replica-",
            ReplicaResourceIdPrefix = $"runtime-container:{resourceSegment}:replica-",
            ReplicaServiceNamePrefix = $"{serviceName}-replica-",
            RuntimeNameScope = normalizedScope
        };
    }

    private static string ResolvePath(
        IHostEnvironment? hostEnvironment,
        string path) =>
        Path.IsPathRooted(path) || hostEnvironment is null
            ? path
            : Path.Combine(hostEnvironment.ContentRootPath, path);

    private static string ResolveCurrentContainerPublishArchitecture() =>
        RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            Architecture.X86 => "x86",
            Architecture.X64 => "x64",
            _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
        };

    private static string CreateResourceSegment(string resourceId)
    {
        var builder = new StringBuilder(resourceId.Length);
        foreach (var character in resourceId.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        var segment = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(segment)
            ? "container-app"
            : segment;
    }

    private static string CreateScopedServiceName(
        string serviceName,
        string? nameScope)
    {
        if (string.IsNullOrWhiteSpace(nameScope))
        {
            return serviceName;
        }

        const string prefix = "cloudshell-";
        var serviceSegment = serviceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? serviceName[prefix.Length..]
            : serviceName;
        var candidate = $"{prefix}{nameScope}-{serviceSegment}";
        if (candidate.Length <= DockerServiceNameMaxLength)
        {
            return candidate;
        }

        var hash = CreateHashSegment(candidate);
        var tailLength = DockerServiceNameMaxLength - prefix.Length - nameScope.Length - hash.Length - 2;
        if (tailLength <= 0)
        {
            return CompactNameSegment(candidate, DockerServiceNameMaxLength);
        }

        var tail = TakeNameTail(serviceSegment, tailLength);
        return $"{prefix}{nameScope}-{tail}-{hash}".Trim('-');
    }

    private static string? CreateNameSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        var segment = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(segment)
            ? null
            : CompactNameSegment(segment, RuntimeNameScopeMaxLength);
    }

    private static string CompactNameSegment(
        string value,
        int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var hash = CreateHashSegment(value);
        var tailLength = maxLength - hash.Length - 1;
        if (tailLength <= 0)
        {
            return hash[..Math.Min(hash.Length, maxLength)];
        }

        var tail = TakeNameTail(value, tailLength);
        return $"{tail}-{hash}".Trim('-');
    }

    private static string TakeNameTail(
        string value,
        int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value.Trim('-');
        }

        var tail = value[^maxLength..].Trim('-');
        var hyphenIndex = tail.IndexOf('-', StringComparison.Ordinal);
        if (hyphenIndex > 0 && hyphenIndex < tail.Length - 1)
        {
            var hyphenAlignedTail = tail[(hyphenIndex + 1)..];
            if (hyphenAlignedTail.Length >= Math.Min(4, maxLength / 2))
            {
                return hyphenAlignedTail;
            }
        }

        return tail;
    }

    private static string CreateHashSegment(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }
}
