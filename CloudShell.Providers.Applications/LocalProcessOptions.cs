namespace CloudShell.Providers.Applications;

public sealed class LocalProcessOptions
{
    public string RuntimeStatePath { get; set; } = "Data/application-runtime-state.json";

    public string LogDirectory { get; set; } = "Data/application-logs";
}
