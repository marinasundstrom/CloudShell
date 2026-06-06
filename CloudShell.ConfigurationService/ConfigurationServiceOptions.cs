namespace CloudShell.ConfigurationService;

public sealed class ConfigurationServiceOptions
{
    public const string SectionName = "CloudShell:ConfigurationService";

    public string DefinitionsPath { get; set; } = "Data/configuration-stores.json";

    public string? ResourceId { get; set; }
}
