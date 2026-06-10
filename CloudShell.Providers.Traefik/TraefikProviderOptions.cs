namespace CloudShell.Providers.Traefik;

public sealed class TraefikProviderOptions
{
    public string DynamicConfigurationDirectory { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "traefik");

    public bool ManageRuntimeContainer { get; set; }

    public string RuntimeContainerImage { get; set; } = "traefik:v3.0";

    public string RuntimeContainerNetwork { get; set; } = "cloudshell";
}
