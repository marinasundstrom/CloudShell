namespace CloudShell.Persistence;

internal sealed class ResourceHealthSnapshotEntity
{
    public long Id { get; set; }

    public string ResourceId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset CheckedAt { get; set; }

    public string ChecksJson { get; set; } = "[]";
}
