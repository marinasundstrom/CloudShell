namespace CloudShell.ControlPlane.Providers;

public sealed class ContainerHostResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<ContainerHostResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        ContainerHostResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        ContainerHostResourceTypeProvider.ProviderId;

    public ContainerHostResourceDefinitionBuilder WithHostKind(string hostKind) =>
        SetScalarAttribute(ContainerHostResourceTypeProvider.Attributes.HostKind, hostKind);

    public ContainerHostResourceDefinitionBuilder WithEndpoint(string endpoint) =>
        SetScalarAttribute(ContainerHostResourceTypeProvider.Attributes.Endpoint, endpoint);

    public ContainerHostResourceDefinitionBuilder WithRegistry(string registry) =>
        SetScalarAttribute(ContainerHostResourceTypeProvider.Attributes.Registry, registry);

    public ContainerHostResourceDefinitionBuilder AsDefault(bool isDefault = true) =>
        SetScalarAttribute(ContainerHostResourceTypeProvider.Attributes.IsDefault, isDefault);

    public ContainerHostResourceDefinitionBuilder UseDocker(
        string endpoint = "unix:///var/run/docker.sock",
        string registry = "docker.io")
    {
        WithHostKind("Docker");
        WithEndpoint(endpoint);
        WithRegistry(registry);
        AsDefault();
        return this;
    }
}

public static class ContainerHostResourceDefinitionBuilderExtensions
{
    public const string DefaultContainerHostResourceId = "cloudshell.container-host:default";

    public static ContainerHostResourceDefinitionBuilder DefaultContainerHost(
        this ResourceGraphBuilder graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        return graph.GetOrAddResource(
            DefaultContainerHostResourceId,
            () => new ContainerHostResourceDefinitionBuilder("default")
                .WithResourceId(DefaultContainerHostResourceId)
                .WithDisplayName("Default container host")
                .UseDocker());
    }

    public static ContainerHostResourceDefinitionBuilder AddContainerHost(
        this ResourceGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new ContainerHostResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
