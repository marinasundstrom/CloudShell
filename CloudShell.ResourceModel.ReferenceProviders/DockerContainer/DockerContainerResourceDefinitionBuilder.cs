namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class DockerContainerResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<DockerContainerResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        DockerContainerResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        DockerContainerResourceTypeProvider.ProviderId;

    public DockerContainerResourceDefinitionBuilder WithImage(string image) =>
        SetScalarAttribute(DockerContainerResourceTypeProvider.Attributes.ContainerImage, image);

    public DockerContainerResourceDefinitionBuilder WithRegistry(string registry) =>
        SetScalarAttribute(DockerContainerResourceTypeProvider.Attributes.ContainerRegistry, registry);

    public DockerContainerResourceDefinitionBuilder WithReplicas(long replicas) =>
        SetScalarAttribute(DockerContainerResourceTypeProvider.Attributes.ContainerReplicas, replicas);

    public DockerContainerResourceDefinitionBuilder WithWorkloadKind(string workloadKind) =>
        SetScalarAttribute(DockerContainerResourceTypeProvider.Attributes.WorkloadKind, workloadKind);

    public DockerContainerResourceDefinitionBuilder WithRuntimeMonitoring() =>
        DeclareCapability(ResourceCommonCapabilityIds.Monitoring);

    public DockerContainerResourceDefinitionBuilder WithRuntimeLogSources() =>
        DeclareCapability(ResourceLogSourceCapabilityIds.LogSources);

    public DockerContainerResourceDefinitionBuilder UseDockerHost(
        IResourceDefinitionBuilder host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return UseDockerHost(host.EffectiveResourceId);
    }

    public DockerContainerResourceDefinitionBuilder UseDockerHost(string hostResourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostResourceId);

        return AddDependency(ResourceReference.DependsOnResourceId(
            hostResourceId,
            DockerHostResourceTypeProvider.ResourceTypeId));
    }
}

public static class DockerContainerResourceDefinitionBuilderExtensions
{
    public static DockerContainerResourceDefinitionBuilder AddDockerContainer(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new DockerContainerResourceDefinitionBuilder(name)
            .WithRuntimeMonitoring()
            .WithRuntimeLogSources();
        graph.Add(builder);
        return builder;
    }
}
