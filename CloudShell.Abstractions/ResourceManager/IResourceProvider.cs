namespace CloudShell.Abstractions.ResourceManager;

public interface IResourceProvider
{
    string Id { get; }

    string DisplayName { get; }

    IReadOnlyList<Resource> GetResources();
}

public interface IResourceModelDiagnosticProvider
{
    IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics();
}
