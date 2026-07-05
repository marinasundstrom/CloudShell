namespace CloudShell.Abstractions.ResourceManager;

public sealed record AppSetting(
    string Name,
    string? Value = null,
    ConfigurationSettingReference? ConfigurationSetting = null,
    SecretReference? Secret = null)
{
    public static AppSetting Literal(string name, string value) =>
        new(name, Value: value);

    public static AppSetting FromConfiguration(
        string name,
        ConfigurationSettingReference configurationSetting) =>
        new(name, ConfigurationSetting: configurationSetting);

    public static AppSetting FromSecret(
        string name,
        SecretReference secret) =>
        new(name, Secret: secret);
}

public sealed record ConfigurationSettingReference(
    string StoreResourceId,
    string SettingName,
    string? Version = null);

public sealed record SecretReference(
    string VaultResourceId,
    string SecretName,
    string? Version = null);

public sealed record CertificateReference(
    string VaultResourceId,
    string CertificateName,
    string? Version = null);

public interface IResourceAppSettingConfigurationProvider
{
    bool CanConfigureAppSettings(Resource resource);

    IReadOnlyList<AppSetting> GetConfiguredAppSettings(string resourceId);

    Task<ResourceProcedureResult> UpdateAppSettingsAsync(
        ResourceProcedureContext context,
        IReadOnlyList<AppSetting> appSettings,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceSettingResolutionContext(
    string TargetResourceId,
    string? ResourceGroupId = null,
    string? Operation = null,
    ResourceIdentityReference? Identity = null,
    string? IdentityDisplayName = null);

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

public sealed class ResourceSettingResolutionException : InvalidOperationException
{
    public ResourceSettingResolutionException(
        string settingName,
        string referenceKind,
        string message)
        : base($"Could not resolve {referenceKind} reference for setting '{settingName}'. {message}")
    {
        SettingName = settingName;
        ReferenceKind = referenceKind;
    }

    public string SettingName { get; }

    public string ReferenceKind { get; }
}

public interface IConfigurationSettingReferenceResolver
{
    ResourceSettingResolutionResult ResolveConfigurationSetting(
        ConfigurationSettingReference reference,
        ResourceSettingResolutionContext context);
}

public interface ISecretReferenceResolver
{
    ValueTask<ResourceSettingResolutionResult> ResolveSecretAsync(
        SecretReference reference,
        ResourceSettingResolutionContext context,
        CancellationToken cancellationToken = default);
}

public sealed record CertificateResolutionResult(
    string? Value,
    string? ContentType = null,
    string? Thumbprint = null,
    string? Subject = null,
    DateTimeOffset? NotBefore = null,
    DateTimeOffset? Expires = null,
    string? ErrorMessage = null)
{
    public bool IsResolved => ErrorMessage is null;

    public static CertificateResolutionResult Resolved(
        string value,
        string? contentType = null,
        string? thumbprint = null,
        string? subject = null,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? expires = null) =>
        new(value, contentType, thumbprint, subject, notBefore, expires);

    public static CertificateResolutionResult Failed(string errorMessage) =>
        new(null, ErrorMessage: errorMessage);
}

public interface ICertificateReferenceResolver
{
    ValueTask<CertificateResolutionResult> ResolveCertificateAsync(
        CertificateReference reference,
        ResourceSettingResolutionContext context,
        CancellationToken cancellationToken = default);
}
