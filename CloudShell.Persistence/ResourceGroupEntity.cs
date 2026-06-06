namespace CloudShell.Persistence;

internal sealed class ResourceGroupEntity
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    public required string Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
