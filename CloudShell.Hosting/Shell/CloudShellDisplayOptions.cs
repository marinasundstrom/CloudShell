namespace CloudShell.Hosting.Shell;

public sealed class CloudShellDisplayOptions
{
    public const string SectionName = "Shell";

    public string ApplicationName { get; set; } = "CloudShell";

    public string? EnvironmentName { get; set; }
}
