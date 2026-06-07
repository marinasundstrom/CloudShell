namespace CloudShell.Providers.Docker;

public sealed record DockerResourceDefinition
{
    public DockerResourceDefinition(
        string id,
        string name)
    {
        Id = DockerContainerResourceProvider.CreateDockerResourceId(id);
        Name = name;
    }

    public string Id { get; init; }

    public string Name { get; init; }
}
