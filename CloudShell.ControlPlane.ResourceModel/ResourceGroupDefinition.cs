namespace CloudShell.ControlPlane.ResourceModel;

public sealed class ResourceGroupDefinition
{
    public ResourceGroupDefinition(
        string id,
        string name,
        string description = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id.Trim();
        Name = name.Trim();
        Description = description.Trim();
    }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }
}
