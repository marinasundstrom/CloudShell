namespace CloudShell.Abstractions.ResourceManager;

public interface IResourceProvider
{
    string Id { get; }

    string DisplayName { get; }

    IReadOnlyList<Resource> GetResources();
}
