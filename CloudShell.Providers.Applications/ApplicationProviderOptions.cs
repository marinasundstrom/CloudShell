namespace CloudShell.Providers.Applications;

public sealed class ApplicationProviderOptions
{
    public string DefinitionsPath { get; set; } = "Data/application-resources.json";

    public IList<ApplicationResourceDefinition> InitialApplications { get; } = [];
}
