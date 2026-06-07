namespace CloudShell.Providers.DockerCompose;

public sealed class DockerComposeOrchestratorOptions
{
    public bool Enabled { get; set; } = true;

    public string ProjectName { get; set; } = "cloudshell";

    public string? ComposeFilePath { get; set; }

    public string? WorkingDirectory { get; set; }

    public string? ContainerEngineId { get; set; }

    public string? ContainerEngineResourceId
    {
        get => ContainerEngineId;
        set => ContainerEngineId = value;
    }

    public string? DockerHostResourceId
    {
        get => ContainerEngineId;
        set => ContainerEngineId = value;
    }

    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public bool GenerateComposeFile { get; set; } = true;

    public string GeneratedComposeFilePath { get; set; } = "Data/docker-compose.generated.yaml";
}
