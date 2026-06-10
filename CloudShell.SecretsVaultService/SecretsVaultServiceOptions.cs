namespace CloudShell.SecretsVaultService;

public sealed class SecretsVaultServiceOptions
{
    public const string SectionName = "CloudShell:SecretsVaultService";

    public string DefinitionsPath { get; set; } = "Data/secrets-vaults.json";

    public string? ResourceId { get; set; }
}
