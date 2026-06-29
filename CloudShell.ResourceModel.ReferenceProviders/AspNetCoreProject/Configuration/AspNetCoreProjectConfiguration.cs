namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed record AspNetCoreProjectConfiguration(
    string ProjectPath,
    string? Arguments = null,
    bool HotReload = true,
    bool UseLaunchSettings = true);
