namespace CloudShell.Providers.DockerCompose;

public sealed class DockerComposeOrchestratorOptions
{
    public bool Enabled { get; set; } = true;

    public string ProjectName { get; set; } = "cloudshell";

    public string? ComposeFilePath { get; set; }

    public string? WorkingDirectory { get; set; }

    public string? ContainerHostId { get; set; }

    public string? ContainerHostResourceId
    {
        get => ContainerHostId;
        set => ContainerHostId = value;
    }

    public string? DockerHostResourceId
    {
        get => ContainerHostId;
        set => ContainerHostId = value;
    }

    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public bool GenerateComposeFile { get; set; } = true;

    public string GeneratedComposeFilePath { get; set; } = "Data/docker-compose.generated.yaml";

    public bool EnableReplicatedContainerAppIngress { get; set; } = true;

    public string ReplicatedContainerAppIngressImage { get; set; } = "traefik:v3.0";
}
