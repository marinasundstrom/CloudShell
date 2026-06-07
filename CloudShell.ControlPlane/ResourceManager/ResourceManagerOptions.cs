namespace CloudShell.ControlPlane.ResourceManager;

public sealed class ResourceManagerOptions
{
    public const string SectionName = "ResourceManager";

    public int? HealthCheckIntervalSeconds { get; set; }
}
