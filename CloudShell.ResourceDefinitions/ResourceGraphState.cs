namespace CloudShell.ResourceDefinitions;

public readonly record struct ResourceGraphVersion(long Value)
{
    public static ResourceGraphVersion Initial { get; } = new(0);

    public ResourceGraphVersion Next() => new(Value + 1);

    public override string ToString() => Value.ToString();
}

public sealed record ResourceGraphSnapshot(
    ResourceGraphVersion Version,
    IReadOnlyList<ResourceState> Resources);

public sealed record ResourceGraphRefreshContext(
    IReadOnlyList<string>? ResourceIds = null)
{
    public static ResourceGraphRefreshContext Full { get; } = new();

    public bool IsFullGraph => ResourceIds is not { Count: > 0 };
}
