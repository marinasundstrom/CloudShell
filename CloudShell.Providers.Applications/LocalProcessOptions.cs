namespace CloudShell.Providers.Applications;

public sealed class LocalProcessOptions
{
    public string RuntimeStatePath { get; set; } = "Data/application-runtime-state.json";

    public string LogStore { get; set; } = ApplicationLogStores.InMemory;

    public string LogDirectory { get; set; } = "Data/application-logs";

    public int LogRetentionDays { get; set; } = 7;

    public int RetainedLogEntries { get; set; } = 1_000;

    public bool SplitLogFilesByDay { get; set; }
}
