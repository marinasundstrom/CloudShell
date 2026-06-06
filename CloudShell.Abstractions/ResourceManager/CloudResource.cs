namespace CloudShell.Abstractions.ResourceManager;

public sealed record CloudResource(
    string Id,
    string Name,
    string Kind,
    string Provider,
    string Region,
    ResourceState State,
    IReadOnlyList<ResourceEndpoint> Endpoints,
    string Version,
    DateTimeOffset LastUpdated,
    IReadOnlyList<string> DependsOn,
    string? DetailRoute = null,
    string? ParentResourceId = null,
    string? TypeId = null)
{
    public string PrimaryEndpoint => Endpoints.FirstOrDefault()?.Address ?? "none";

    public string EffectiveTypeId => TypeId ?? Kind;
}

public enum ResourceState
{
    Running,
    Starting,
    Degraded,
    Stopped,
    Unknown
}
