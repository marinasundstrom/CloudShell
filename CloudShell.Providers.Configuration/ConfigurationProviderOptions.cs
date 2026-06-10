namespace CloudShell.Providers.Configuration;

public sealed class ConfigurationProviderOptions
{
    public string DefinitionsPath { get; set; } = "Data/configuration-stores.json";

    public string PublicBaseUrl { get; set; } = "http://localhost:5047";

    public string ServiceUrlScheme { get; set; } = "http";

    public string ServiceHost { get; set; } = "localhost";

    public int ServiceBasePort { get; set; } = 5138;

    public string ServiceProcessIdPrefix { get; set; } = "configuration-service";

    [Obsolete("Use ServiceProcessIdPrefix instead.")]
    public string ServiceResourceIdPrefix
    {
        get => ServiceProcessIdPrefix;
        set => ServiceProcessIdPrefix = value;
    }

    public string ServiceExecutablePath { get; set; } = "dotnet";

    public string? ServiceProjectPath { get; set; }

    public string? ServiceWorkingDirectory { get; set; }

    public IList<ConfigurationStoreDefinition> InitialStores { get; } = [];

    internal IList<DeclaredConfigurationStore> DeclaredStores { get; } = [];

    internal IList<DeclaredHostConfigurationSource> DeclaredHostConfigurationSources { get; } = [];

    internal IList<DeclaredSecretsVault> DeclaredSecretsVaults { get; } = [];
}

internal sealed class DeclaredConfigurationStore(ConfigurationStoreDefinition definition)
{
    public ConfigurationStoreDefinition Definition { get; set; } = definition;

    public bool Persist { get; set; }

    public bool OverwritePersistedState { get; set; }
}

internal sealed class DeclaredHostConfigurationSource(HostConfigurationSourceDefinition definition)
{
    public HostConfigurationSourceDefinition Definition { get; set; } = definition;
}

internal sealed class DeclaredSecretsVault(SecretsVaultDefinition definition)
{
    public SecretsVaultDefinition Definition { get; set; } = definition;
}
