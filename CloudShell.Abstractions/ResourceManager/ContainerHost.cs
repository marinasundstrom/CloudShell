namespace CloudShell.Abstractions.ResourceManager;

public static class ContainerRegistryDefaults
{
    public const string DockerHub = "docker.io";

    public const string Default = DockerHub;

    public const string Local = "http://localhost:5000";
}

public static class ContainerHostResourceTypes
{
    public const string ContainerHost = "cloudshell.container-host";
}

public enum ContainerHostKind
{
    Docker,
    Podman,
    DockerCompatible,
    Kubernetes,
    Process,
    Custom
}

public sealed record ContainerHostDescriptor(
    string Id,
    string Name,
    ContainerHostKind Kind,
    string Endpoint,
    bool IsDefault = false,
    string Registry = ContainerRegistryDefaults.Default,
    ContainerRegistryCredentials? RegistryCredentials = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public IReadOnlyDictionary<string, string> HostMetadata => Metadata ?? EmptyMetadata;

    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public interface IContainerHostProvider
{
    ContainerHostDescriptor GetDefaultHost();
}

public interface IContainerHostResolver
{
    Task<ContainerHostResolutionResult> ResolveAsync(
        ContainerHostResolutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ContainerHostResolutionRequest(
    string TargetResourceId,
    string? ResourceGroupId,
    string? ExplicitHostResourceId = null,
    string? PreferredHostId = null,
    string? RequiredCapability = null);

public sealed record ContainerHostResolutionResult(
    ContainerHostDescriptor? Host,
    string? ErrorMessage = null)
{
    public bool IsResolved => Host is not null && ErrorMessage is null;
}
