namespace CloudShell.Abstractions.ResourceManager;

public interface IResourceManagerStore
{
    IReadOnlyList<IResourceProvider> Providers { get; }

    IReadOnlyList<ResourceGroup> GetResourceGroups();

    IReadOnlyList<Resource> GetAvailableResources();

    IReadOnlyList<Resource> GetResources();

    IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics();

    ResourceClass? GetResourceTypeClass(string resourceType);

    Resource? GetResource(string id);

    IReadOnlyList<Resource> GetChildren(string resourceId);

    ResourceGroup? GetGroupForResource(string resourceId);

    bool IsRegistered(string resourceId);
}
