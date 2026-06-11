namespace CloudShell.Abstractions.ResourceManager;

public sealed record AppSetting(
    string Name,
    string? Value = null,
    ConfigurationEntryReference? ConfigurationEntry = null,
    SecretReference? Secret = null)
{
    public static AppSetting Literal(string name, string value) =>
        new(name, Value: value);

    public static AppSetting FromConfiguration(
        string name,
        ConfigurationEntryReference configurationEntry) =>
        new(name, ConfigurationEntry: configurationEntry);

    public static AppSetting FromSecret(
        string name,
        SecretReference secret) =>
        new(name, Secret: secret);
}

public sealed record ConfigurationEntryReference(
    string StoreResourceId,
    string EntryName,
    string? Version = null);

public sealed record SecretReference(
    string VaultResourceId,
    string SecretName,
    string? Version = null);

public sealed record ResourceSettingResolutionContext(
    string TargetResourceId,
    string? ResourceGroupId = null,
    string? Operation = null,
    ResourceIdentityReference? Identity = null);

public sealed record ResourceSettingResolutionResult(
    string? Value,
    string? ErrorMessage = null)
{
    public bool IsResolved => ErrorMessage is null;

    public static ResourceSettingResolutionResult Resolved(string value) =>
        new(value);

    public static ResourceSettingResolutionResult Failed(string errorMessage) =>
        new(null, errorMessage);
}

public interface IConfigurationEntryReferenceResolver
{
    ResourceSettingResolutionResult ResolveConfigurationEntry(
        ConfigurationEntryReference reference,
        ResourceSettingResolutionContext context);
}

public interface ISecretReferenceResolver
{
    ValueTask<ResourceSettingResolutionResult> ResolveSecretAsync(
        SecretReference reference,
        ResourceSettingResolutionContext context,
        CancellationToken cancellationToken = default);
}
