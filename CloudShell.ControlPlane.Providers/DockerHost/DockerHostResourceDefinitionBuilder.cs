namespace CloudShell.ControlPlane.Providers;

public sealed class DockerHostResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<DockerHostResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        DockerHostResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        DockerHostResourceTypeProvider.ProviderId;

    public DockerHostResourceDefinitionBuilder WithHostKind(string hostKind) =>
        SetScalarAttribute(DockerHostResourceTypeProvider.Attributes.HostKind, hostKind);

    public DockerHostResourceDefinitionBuilder WithEndpoint(string endpoint) =>
        SetScalarAttribute(DockerHostResourceTypeProvider.Attributes.Endpoint, endpoint);

    public DockerHostResourceDefinitionBuilder WithRegistry(string registry) =>
        SetScalarAttribute(DockerHostResourceTypeProvider.Attributes.Registry, registry);

    public DockerHostResourceDefinitionBuilder AsDefault(bool isDefault = true) =>
        SetScalarAttribute(DockerHostResourceTypeProvider.Attributes.IsDefault, isDefault);

    public DockerHostResourceDefinitionBuilder UseLocalDocker(
        string endpoint = "unix:///var/run/docker.sock",
        string registry = "docker.io")
    {
        WithHostKind("local");
        WithEndpoint(endpoint);
        WithRegistry(registry);
        AsDefault();
        return this;
    }
}

public static class DockerHostResourceDefinitionBuilderExtensions
{
    public static DockerHostResourceDefinitionBuilder AddDockerHost(
        this ResourceGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new DockerHostResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
