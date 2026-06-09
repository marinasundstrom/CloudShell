namespace CloudShell.Providers.Traefik;

public sealed class TraefikProviderOptions
{
    public string DynamicConfigurationDirectory { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "traefik");
}
