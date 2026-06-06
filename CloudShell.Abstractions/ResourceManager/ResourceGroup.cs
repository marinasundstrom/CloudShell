namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceGroup(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> ResourceIds);
