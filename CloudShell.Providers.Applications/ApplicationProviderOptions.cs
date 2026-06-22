namespace CloudShell.Providers.Applications;

public sealed class ApplicationProviderOptions
{
    public string DefinitionsPath { get; set; } = "Data/application-resources.json";

    public string RuntimeStatePath { get; set; } = "Data/application-runtime-state.json";

    public string ContainerDeploymentHistoryPath { get; set; } = "Data/application-container-deployments.json";

    public string LogStore { get; set; } = ApplicationLogStores.InMemory;

    public string LogDirectory { get; set; } = "Data/application-logs";

    public int LogRetentionDays { get; set; } = 7;

    public int RetainedLogEntries { get; set; } = 1_000;

    public bool SplitLogFilesByDay { get; set; }

    public string IngressConfigurationDirectory { get; set; } = "Data/application-ingress";

    public bool EnableReplicatedContainerAppIngress { get; set; } = true;

    public string ReplicatedContainerAppIngressImage { get; set; } = "traefik:v3.0";

    public int AutoLocalPortStart { get; set; } = 20000;

    public int AutoLocalPortEnd { get; set; } = 29999;

    public bool EnableObservabilityByDefault { get; set; } = true;

    public TimeSpan ContainerStartConfirmationDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    public string? OtlpEndpoint { get; set; }

    public string? OtlpProtocol { get; set; }

    public string? OtlpHeaders { get; set; }

    public string? ResourceIdentityTokenEndpoint { get; set; }

    public string ResourceIdentityDefaultScope { get; set; } = "ControlPlane.Access";

    public IList<ApplicationResourceDefinition> InitialApplications { get; } = [];

    internal IList<DeclaredApplicationResource> DeclaredApplications { get; } = [];
}

public static class ApplicationLogStores
{
    public const string InMemory = "InMemory";

    public const string File = "File";
}

internal sealed class DeclaredApplicationResource(ApplicationResourceDefinition definition)
{
    public ApplicationResourceDefinition Definition { get; set; } = definition;

    public bool Persist { get; set; }

    public bool OverwritePersistedState { get; set; }
}
