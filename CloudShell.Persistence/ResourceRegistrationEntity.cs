namespace CloudShell.Persistence;

internal sealed class ResourceRegistrationEntity
{
    public required string ResourceId { get; set; }

    public required string ProviderId { get; set; }

    public string? ResourceGroupId { get; set; }

    public DateTimeOffset RegisteredAt { get; set; }
}
