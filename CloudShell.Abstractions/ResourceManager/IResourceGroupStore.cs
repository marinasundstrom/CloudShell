namespace CloudShell.Abstractions.ResourceManager;

public interface IResourceGroupStore
{
    IReadOnlyList<ResourceGroup> GetResourceGroups();

    ResourceGroup? GetGroupForResource(string resourceId);

    Task<ResourceGroup> CreateAsync(
        string name,
        string description,
        CancellationToken cancellationToken = default);
}
