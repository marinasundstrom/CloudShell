namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceEndpoint(
    string Name,
    string Address,
    string Protocol,
    bool IsExternal);
