using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class ResourceManagerOptions
{
    public const string SectionName = "ResourceManager";

    public int? HealthCheckIntervalSeconds { get; set; }

    public DependencyStartFailureBehavior? DependencyStartFailureBehavior { get; set; }

    public bool AllowLocalPathResourceDefinitions { get; set; }
}
