namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class LocalVolumeResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<LocalVolumeResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        LocalVolumeResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        LocalVolumeResourceTypeProvider.ProviderId;

    public LocalVolumeResourceDefinitionBuilder WithStorageMedium(string storageMedium) =>
        SetScalarAttribute(LocalVolumeResourceTypeProvider.Attributes.StorageMedium, storageMedium);
}

public static class LocalVolumeResourceDefinitionBuilderExtensions
{
    public static LocalVolumeResourceDefinitionBuilder AddLocalVolume(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new LocalVolumeResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
