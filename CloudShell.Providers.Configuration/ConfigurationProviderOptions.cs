namespace CloudShell.Providers.Configuration;

public sealed class ConfigurationProviderOptions
{
    public string DefinitionsPath { get; set; } = "Data/configuration-stores.json";

    public string PublicBaseUrl { get; set; } = "http://localhost:5047";

    public IList<ConfigurationStoreDefinition> InitialStores { get; } = [];

    internal IList<DeclaredConfigurationStore> DeclaredStores { get; } = [];
}

internal sealed class DeclaredConfigurationStore(ConfigurationStoreDefinition definition)
{
    public ConfigurationStoreDefinition Definition { get; set; } = definition;

    public bool Persist { get; set; }

    public bool OverwritePersistedState { get; set; }
}
