namespace CloudShell.Abstractions.ResourceManager;

public sealed record Resource(
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
    string? TypeId = null,
    IReadOnlyList<ResourceAction>? Actions = null,
    IReadOnlyList<ResourceHealthCheck>? HealthChecks = null,
    ResourceObservability? Observability = null,
    ResourceClass ResourceClass = ResourceClass.Generic)
{
    public string PrimaryEndpoint => Endpoints.FirstOrDefault()?.Address ?? "none";

    public string EffectiveTypeId => TypeId ?? Kind;

    public IReadOnlyList<ResourceAction> ResourceActions => Actions ?? [];

    public ResourceAction? GetAction(string actionId) =>
        ResourceActions.FirstOrDefault(action =>
            string.Equals(action.Id, actionId, StringComparison.OrdinalIgnoreCase));

    public bool HasAction(string actionId) => GetAction(actionId) is not null;

    public ResourceAction? RunAction => GetAction(ResourceActionIds.Run);

    public ResourceAction? StopAction => GetAction(ResourceActionIds.Stop);

    public ResourceAction? PauseAction => GetAction(ResourceActionIds.Pause);

    public ResourceAction? RestartAction => GetAction(ResourceActionIds.Restart);

    public IReadOnlyList<ResourceHealthCheck> ResourceHealthChecks => HealthChecks ?? [];

    public ResourceObservability EffectiveObservability => Observability ?? ResourceObservability.None;
}

public enum ResourceClass
{
    Generic,
    Executable,
    Project,
    Container,
    Service,
    Network,
    Configuration,
    Infrastructure
}

public enum ResourceState
{
    Running,
    Starting,
    Paused,
    Degraded,
    Stopped,
    Unknown
}
