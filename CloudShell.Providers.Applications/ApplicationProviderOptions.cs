namespace CloudShell.Providers.Applications;

public sealed class ApplicationProviderOptions
{
    public string DefinitionsPath { get; set; } = "Data/application-resources.json";

    public string RuntimeStatePath { get; set; } = "Data/application-runtime-state.json";

    public string LogDirectory { get; set; } = "Data/application-logs";

    public int AutoLocalPortStart { get; set; } = 20000;

    public int AutoLocalPortEnd { get; set; } = 29999;

    public bool EnableObservabilityByDefault { get; set; } = true;

    public string? OtlpEndpoint { get; set; }

    public string? OtlpProtocol { get; set; }

    public string? OtlpHeaders { get; set; }

    public IList<ApplicationResourceDefinition> InitialApplications { get; } = [];

    internal IList<DeclaredApplicationResource> DeclaredApplications { get; } = [];
}

internal sealed class DeclaredApplicationResource(ApplicationResourceDefinition definition)
{
    public ApplicationResourceDefinition Definition { get; set; } = definition;

    public bool Persist { get; set; }

    public bool OverwritePersistedState { get; set; }
}
