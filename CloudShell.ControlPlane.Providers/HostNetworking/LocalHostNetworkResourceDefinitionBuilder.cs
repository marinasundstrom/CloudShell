namespace CloudShell.ControlPlane.Providers;

public sealed class LocalHostNetworkResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<LocalHostNetworkResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        LocalHostNetworkResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        LocalHostNetworkResourceTypeProvider.ProviderId;

    public LocalHostNetworkResourceDefinitionBuilder WithInfrastructureKind(string infrastructureKind) =>
        SetScalarAttribute(LocalHostNetworkResourceTypeProvider.Attributes.InfrastructureKind, infrastructureKind);

    public LocalHostNetworkResourceDefinitionBuilder WithHostReadiness(string hostReadiness) =>
        SetScalarAttribute(LocalHostNetworkResourceTypeProvider.Attributes.HostReadiness, hostReadiness);

    public LocalHostNetworkResourceDefinitionBuilder WithHostOperatingSystem(string operatingSystem) =>
        SetScalarAttribute(LocalHostNetworkResourceTypeProvider.Attributes.HostOperatingSystem, operatingSystem);

    public LocalHostNetworkResourceDefinitionBuilder WithNetworkingMode(string networkingMode) =>
        SetScalarAttribute(LocalHostNetworkResourceTypeProvider.Attributes.NetworkingMode, networkingMode);
}

public static class LocalHostNetworkResourceDefinitionBuilderExtensions
{
    public static LocalHostNetworkResourceDefinitionBuilder AddLocalHostNetwork(
        this ResourceGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new LocalHostNetworkResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
