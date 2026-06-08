namespace CloudShell.Abstractions.ResourceManager;

public static class ContainerEngineResourceTypes
{
    public const string ContainerEngine = "cloudshell.container-engine";
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
    string Registry = "local");

public interface IContainerEngineProvider
{
    ContainerEngineResourceDefinition GetContainerEngine();
}
