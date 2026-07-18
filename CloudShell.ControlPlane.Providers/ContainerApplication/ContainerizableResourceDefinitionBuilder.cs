namespace CloudShell.ControlPlane.Providers;

public abstract class ContainerizableResourceDefinitionBuilder<TBuilder>(
    string name,
    ResourceTypeId defaultTypeId,
    string? defaultProviderId) :
    ResourceDefinitionBuilder<TBuilder>(name)
    where TBuilder : ContainerizableResourceDefinitionBuilder<TBuilder>
{
    private ResourceTypeId _typeId = defaultTypeId;
    private string? _providerId = defaultProviderId;

    protected override ResourceTypeId TypeId => _typeId;

    protected override string? ProviderId => _providerId;

    protected bool IsContainerApplication =>
        TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId;

    protected TBuilder ProjectAsContainerApplication(
        string image,
        string? registry = null,
        string? buildContext = null,
        string? dockerfile = null,
        ResourceAttributeId? sourceEndpointAttribute = null,
        IReadOnlyList<NetworkingEndpointRequestValue>? endpointRequests = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);

        _typeId = ContainerApplicationResourceTypeProvider.ResourceTypeId;
        _providerId = ContainerApplicationResourceTypeProvider.ProviderId;
        ResourceGraph?.AddResourceTypeDefinition(new ContainerApplicationResourceTypeProvider().TypeDefinition);
        ResourceGraph?.AddResourceCapabilityAttributeProvider(new EnvironmentVariablesCapabilityProvider());
        ResourceGraph?.AddResourceCapabilityAttributeProvider(new VolumeConsumerCapabilityProvider());

        SetScalarAttribute(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerImage,
            image);
        if (!Attributes.ContainsKey(ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas))
        {
            SetScalarAttribute(
                ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas,
                1);
        }

        if (!string.IsNullOrWhiteSpace(registry))
        {
            SetScalarAttribute(
                ContainerApplicationResourceTypeProvider.Attributes.ContainerRegistry,
                registry);
        }

        if (!string.IsNullOrWhiteSpace(buildContext))
        {
            SetScalarAttribute(
                ContainerApplicationResourceTypeProvider.Attributes.ContainerBuildContext,
                buildContext);
            SetScalarAttribute(
                ResourceAttributeId.Create("project.path"),
                buildContext);
        }

        if (!string.IsNullOrWhiteSpace(dockerfile))
        {
            SetScalarAttribute(
                ContainerApplicationResourceTypeProvider.Attributes.ContainerDockerfile,
                dockerfile);
        }

        if (sourceEndpointAttribute is { } attribute)
        {
            RemoveAttribute(attribute);
        }

        if (endpointRequests is { Count: > 0 })
        {
            SetObjectAttribute(
                ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests,
                endpointRequests.ToArray());
        }

        return Self;
    }

    protected TBuilder SetContainerReplicas(long replicas)
    {
        SetScalarAttribute(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas,
            replicas);
        return Self;
    }

    protected override void OnBeforeBuild()
    {
        if (!IsContainerApplication ||
            ResourceGraph is not { } graph)
        {
            return;
        }

        graph.AddResourceTypeDefinition(new ContainerApplicationResourceTypeProvider().TypeDefinition);
        graph.AddResourceCapabilityAttributeProvider(new EnvironmentVariablesCapabilityProvider());
        graph.AddResourceCapabilityAttributeProvider(new VolumeConsumerCapabilityProvider());

        if (HasContainerHostDependency())
        {
            return;
        }

        AddDependency(ResourceReference.DependsOnResourceId(
            graph.GetContainerHost().EffectiveResourceId,
            ContainerHostResourceTypeProvider.ResourceTypeId));
    }

    private bool HasContainerHostDependency() =>
        Dependencies.Any(reference =>
            reference.TypeId is { } typeId &&
            ContainerApplicationResourceTypeProvider.IsContainerHostResourceType(typeId));

    private TBuilder Self => (TBuilder)this;
}
