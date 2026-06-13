using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal static class ApplicationSettingReferenceDisplay
{
    public static ApplicationSettingDisplayRow Create(
        AppSetting setting,
        string applicationResourceId,
        ResourceIdentityBinding? identityBinding,
        Func<string, Resource?> resolveResource)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationResourceId);
        ArgumentNullException.ThrowIfNull(resolveResource);

        if (setting.ConfigurationEntry is not null)
        {
            return CreateConfigurationReferenceRow(
                setting.Name,
                setting.ConfigurationEntry,
                applicationResourceId,
                identityBinding,
                resolveResource);
        }

        if (setting.Secret is not null)
        {
            return CreateSecretReferenceRow(
                setting.Name,
                setting.Secret,
                applicationResourceId,
                identityBinding,
                resolveResource);
        }

        return new ApplicationSettingDisplayRow(
            setting.Name,
            "Literal value",
            string.IsNullOrEmpty(setting.Value) ? "empty" : setting.Value,
            null,
            "Visible",
            "ok");
    }

    public static ApplicationSettingDisplayRow Create(
        EnvironmentVariableAssignment assignment,
        string applicationResourceId,
        ResourceIdentityBinding? identityBinding,
        Func<string, Resource?> resolveResource)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationResourceId);
        ArgumentNullException.ThrowIfNull(resolveResource);

        if (assignment.ConfigurationEntry is not null)
        {
            return CreateConfigurationReferenceRow(
                assignment.Name,
                assignment.ConfigurationEntry,
                applicationResourceId,
                identityBinding,
                resolveResource);
        }

        if (assignment.Secret is not null)
        {
            return CreateSecretReferenceRow(
                assignment.Name,
                assignment.Secret,
                applicationResourceId,
                identityBinding,
                resolveResource);
        }

        return new ApplicationSettingDisplayRow(
            assignment.Name,
            "Literal value",
            string.IsNullOrEmpty(assignment.Value) ? "empty" : assignment.Value,
            null,
            "Visible",
            "ok");
    }

    private static ApplicationSettingDisplayRow CreateConfigurationReferenceRow(
        string name,
        ConfigurationEntryReference reference,
        string applicationResourceId,
        ResourceIdentityBinding? identityBinding,
        Func<string, Resource?> resolveResource)
    {
        var resource = resolveResource(reference.StoreResourceId);
        var detail = FormatReferenceDetail(
            reference.StoreResourceId,
            reference.Version,
            resource,
            identityBinding,
            applicationResourceId,
            ConfigurationStoreResourceOperationPermissions.ReadEntries);
        var status = GetReferenceStatus(resource, identityBinding, grantRequiredStatus: "Grant required");

        return new ApplicationSettingDisplayRow(
            name,
            "Configuration entry",
            FormatReferenceTarget(reference.StoreResourceId, reference.EntryName, resource),
            detail,
            status.Text,
            status.Kind);
    }

    private static ApplicationSettingDisplayRow CreateSecretReferenceRow(
        string name,
        SecretReference reference,
        string applicationResourceId,
        ResourceIdentityBinding? identityBinding,
        Func<string, Resource?> resolveResource)
    {
        var resource = resolveResource(reference.VaultResourceId);
        var detail = FormatReferenceDetail(
            reference.VaultResourceId,
            reference.Version,
            resource,
            identityBinding,
            applicationResourceId,
            SecretsVaultResourceOperationPermissions.ReadSecrets);
        var status = GetReferenceStatus(resource, identityBinding, grantRequiredStatus: "Grant required");

        return new ApplicationSettingDisplayRow(
            name,
            "Secret reference",
            FormatReferenceTarget(reference.VaultResourceId, reference.SecretName, resource),
            detail,
            status.Text,
            status.Kind);
    }

    private static (string Text, string Kind) GetReferenceStatus(
        Resource? resource,
        ResourceIdentityBinding? identityBinding,
        string grantRequiredStatus)
    {
        if (resource is null)
        {
            return ("Unavailable", "warning");
        }

        return identityBinding is null
            ? ("Reference", "info")
            : (grantRequiredStatus, "info");
    }

    private static string FormatReferenceTarget(
        string resourceId,
        string itemName,
        Resource? resource) =>
        $"{resource?.Name ?? resourceId} / {itemName}";

    private static string FormatReferenceDetail(
        string resourceId,
        string? version,
        Resource? resource,
        ResourceIdentityBinding? identityBinding,
        string applicationResourceId,
        string requiredPermission)
    {
        var detail = resource is null
            ? $"{resourceId} (unavailable)"
            : resource.Id;
        if (!string.IsNullOrWhiteSpace(version))
        {
            detail = $"{detail}; version {version}";
        }

        if (resource is not null && identityBinding is not null)
        {
            detail = $"{detail}; requires {requiredPermission} for {FormatIdentity(applicationResourceId, identityBinding)}";
        }

        return detail;
    }

    private static string FormatIdentity(
        string applicationResourceId,
        ResourceIdentityBinding identityBinding) =>
        string.IsNullOrWhiteSpace(identityBinding.Name)
            ? applicationResourceId
            : $"{applicationResourceId}/{identityBinding.Name}";
}

internal sealed record ApplicationSettingDisplayRow(
    string Name,
    string Source,
    string Target,
    string? Detail,
    string Status,
    string StatusKind)
{
    public string StatusCssClass => $"reference-status reference-status-{StatusKind}";
}
