namespace CloudShell.Hosting.ResourceManager;

public sealed class ResourceManagerUiOptions
{
    public const string SectionName = "ResourceManager";

    public bool ReadOnly { get; set; }

    public bool ShowRuntimeManagedResources { get; set; }

    public bool ShowHiddenResources { get; set; }
}
