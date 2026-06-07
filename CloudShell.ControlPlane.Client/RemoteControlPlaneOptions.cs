namespace CloudShell.ControlPlane.Client;

public sealed class RemoteControlPlaneOptions
{
    public const string SectionName = "CloudShell:ControlPlane";

    public Uri? BaseAddress { get; set; }

    public RemoteControlPlaneCredentialOptions Credential { get; set; } = new();
}

public sealed class RemoteControlPlaneCredentialOptions
{
    public string Mode { get; set; } = "None";

    public string? BearerToken { get; set; }

    public Uri? TokenEndpoint { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string[] Scopes { get; set; } = ["ControlPlane.Access"];
}
