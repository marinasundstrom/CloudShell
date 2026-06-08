namespace CloudShell.Abstractions.ResourceManager;

public static class ContainerEngineResourceTypes
{
    public const string ContainerEngine = "cloudshell.container-engine";
}

public static class ContainerRegistryDefaults
{
    public const string Local = "http://localhost:5000";
}

public enum ContainerEngineKind
{
    Docker,
    Podman,
    DockerCompatible
}

public sealed record ContainerEngineResourceDefinition(
    string Id,
    string Name,
    ContainerEngineKind Kind,
    string Endpoint,
    bool IsDefault = false,
    string Registry = ContainerRegistryDefaults.Local);

public interface IContainerEngineProvider
{
    ContainerEngineResourceDefinition GetContainerEngine();
}
