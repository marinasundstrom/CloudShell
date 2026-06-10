namespace CloudShell.ConfigurationStoreService;

public sealed class ConfigurationStoreServiceOptions
{
    public const string SectionName = "CloudShell:ConfigurationStoreService";

    public string DefinitionsPath { get; set; } = "Data/configuration-stores.json";

    public string? ResourceId { get; set; }
}
