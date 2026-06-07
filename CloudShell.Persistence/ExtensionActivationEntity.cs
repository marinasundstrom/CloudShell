namespace CloudShell.Persistence;

public sealed class ExtensionActivationEntity
{
    public string ExtensionId { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }
}
