namespace CloudShell.Abstractions.ResourceManager;

public static class ContainerEngineResourceTypes
{
    public const string ContainerEngine = "cloudshell.container-engine";
}

public static class ContainerRegistryDefaults
{
    public const string DockerHub = "docker.io";

    public const string Default = DockerHub;

    public const string Local = "http://localhost:5000";
}

public enum ContainerEngineKind
{
    Docker,
    Podman,
    DockerCompatible
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

public sealed record ContainerEngineResourceDefinition(
    string Id,
    string Name,
    ContainerEngineKind Kind,
    string Endpoint,
    bool IsDefault = false,
    string Registry = ContainerRegistryDefaults.Default,
    ContainerRegistryCredentials? RegistryCredentials = null);

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

public interface IContainerEngineProvider
{
    ContainerEngineResourceDefinition GetContainerEngine();
}

public interface IContainerHostProvider
{
    ContainerHostDescriptor GetDefaultHost();
}

public static class ContainerHostCompatibility
{
    public static ContainerHostDescriptor ToContainerHostDescriptor(
        this ContainerEngineResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new ContainerHostDescriptor(
            definition.Id,
            definition.Name,
            definition.Kind.ToContainerHostKind(),
            definition.Endpoint,
            definition.IsDefault,
            definition.Registry,
            definition.RegistryCredentials);
    }

    public static ContainerEngineResourceDefinition ToContainerEngineResourceDefinition(
        this ContainerHostDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return descriptor.TryToContainerEngineResourceDefinition(out var definition)
            ? definition
            : throw new InvalidOperationException(
                $"Container host kind '{descriptor.Kind}' cannot be represented as a container engine.");
    }

    public static bool TryToContainerEngineResourceDefinition(
        this ContainerHostDescriptor descriptor,
        out ContainerEngineResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!descriptor.Kind.TryToContainerEngineKind(out var kind))
        {
            definition = null!;
            return false;
        }

        definition = new ContainerEngineResourceDefinition(
            descriptor.Id,
            descriptor.Name,
            kind,
            descriptor.Endpoint,
            descriptor.IsDefault,
            descriptor.Registry,
            descriptor.RegistryCredentials);
        return true;
    }

    public static ContainerHostKind ToContainerHostKind(this ContainerEngineKind kind) =>
        kind switch
        {
            ContainerEngineKind.Docker => ContainerHostKind.Docker,
            ContainerEngineKind.Podman => ContainerHostKind.Podman,
            ContainerEngineKind.DockerCompatible => ContainerHostKind.DockerCompatible,
            _ => ContainerHostKind.Custom
        };

    public static bool TryToContainerEngineKind(
        this ContainerHostKind kind,
        out ContainerEngineKind engineKind)
    {
        switch (kind)
        {
            case ContainerHostKind.Docker:
                engineKind = ContainerEngineKind.Docker;
                return true;
            case ContainerHostKind.Podman:
                engineKind = ContainerEngineKind.Podman;
                return true;
            case ContainerHostKind.DockerCompatible:
                engineKind = ContainerEngineKind.DockerCompatible;
                return true;
            default:
                engineKind = default;
                return false;
        }
    }
}
