namespace CloudShell.Abstractions.ResourceManager;

public interface IResourceManagerStore
{
    IReadOnlyList<IResourceProvider> Providers { get; }

    IReadOnlyList<ResourceGroup> GetResourceGroups();

    IReadOnlyList<CloudResource> GetAvailableResources();

    IReadOnlyList<CloudResource> GetResources();

    CloudResource? GetResource(string id);

    IReadOnlyList<CloudResource> GetChildren(string resourceId);

    ResourceGroup? GetGroupForResource(string resourceId);

    bool IsRegistered(string resourceId);
}
