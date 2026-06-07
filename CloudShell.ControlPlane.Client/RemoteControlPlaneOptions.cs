namespace CloudShell.ControlPlane.Client;

public sealed class RemoteControlPlaneOptions
{
    public const string SectionName = "CloudShell:ControlPlane";

    public Uri? BaseAddress { get; set; }
}
