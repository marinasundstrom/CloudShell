namespace CloudShell.Providers.Applications;

public sealed class ApplicationProviderOptions
{
    public string DefinitionsPath { get; set; } = "Data/application-resources.json";

    public string RuntimeStatePath { get; set; } = "Data/application-runtime-state.json";

    public string LogDirectory { get; set; } = "Data/application-logs";

    public IList<ApplicationResourceDefinition> InitialApplications { get; } = [];

    internal IList<DeclaredApplicationResource> DeclaredApplications { get; } = [];
}

internal sealed class DeclaredApplicationResource(ApplicationResourceDefinition definition)
{
    public ApplicationResourceDefinition Definition { get; set; } = definition;

    public bool Persist { get; set; }

    public bool OverwritePersistedState { get; set; }
}
