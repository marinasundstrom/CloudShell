using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal static class ApplicationSettingReferenceDisplay
{
    public static ApplicationSettingDisplayRow Create(
        AppSetting setting,
        string applicationResourceId,
        ResourceIdentityBinding? identityBinding,
        Func<string, Resource?> resolveResource,
        ResourcePermissionGrantEvaluator? grantEvaluator = null)
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
                resolveResource,
                grantEvaluator);
        }

        if (setting.Secret is not null)
        {
            return CreateSecretReferenceRow(
                setting.Name,
                setting.Secret,
                applicationResourceId,
                identityBinding,
                resolveResource,
                grantEvaluator);
        }

        return new ApplicationSettingDisplayRow(
            setting.Name,
            "Literal value",
            string.IsNullOrEmpty(setting.Value) ? "empty" : setting.Value,
            null,
            ResourceSettingDisplay.IsSensitiveLiteralName(setting.Name) ? "Hidden" : "Visible",
            ResourceSettingDisplay.IsSensitiveLiteralName(setting.Name) ? "info" : "ok",
            ResourceSettingDisplay.IsSensitiveLiteralName(setting.Name));
    }

    public static ApplicationSettingDisplayRow Create(
        EnvironmentVariableAssignment assignment,
        string applicationResourceId,
        ResourceIdentityBinding? identityBinding,
        Func<string, Resource?> resolveResource,
        ResourcePermissionGrantEvaluator? grantEvaluator = null)
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
                resolveResource,
                grantEvaluator);
        }

        if (assignment.Secret is not null)
        {
            return CreateSecretReferenceRow(
                assignment.Name,
                assignment.Secret,
                applicationResourceId,
                identityBinding,
                resolveResource,
                grantEvaluator);
        }

        return new ApplicationSettingDisplayRow(
            assignment.Name,
            "Literal value",
            string.IsNullOrEmpty(assignment.Value) ? "empty" : assignment.Value,
            null,
            ResourceSettingDisplay.IsSensitiveLiteralName(assignment.Name) ? "Hidden" : "Visible",
            ResourceSettingDisplay.IsSensitiveLiteralName(assignment.Name) ? "info" : "ok",
            ResourceSettingDisplay.IsSensitiveLiteralName(assignment.Name));
    }

    private static ApplicationSettingDisplayRow CreateConfigurationReferenceRow(
        string name,
        ConfigurationEntryReference reference,
        string applicationResourceId,
        ResourceIdentityBinding? identityBinding,
        Func<string, Resource?> resolveResource,
        ResourcePermissionGrantEvaluator? grantEvaluator)
    {
        var resource = resolveResource(reference.StoreResourceId);
        var detail = FormatReferenceDetail(
            reference.StoreResourceId,
            reference.Version,
            resource,
            identityBinding,
            applicationResourceId,
            ConfigurationStoreResourceOperationPermissions.ReadEntries);
        var status = GetReferenceStatus(
            resource,
            identityBinding,
            applicationResourceId,
            ConfigurationStoreResourceOperationPermissions.ReadEntries,
            grantEvaluator);

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
        Func<string, Resource?> resolveResource,
        ResourcePermissionGrantEvaluator? grantEvaluator)
    {
        var resource = resolveResource(reference.VaultResourceId);
        var detail = FormatReferenceDetail(
            reference.VaultResourceId,
            reference.Version,
            resource,
            identityBinding,
            applicationResourceId,
            SecretsVaultResourceOperationPermissions.ReadSecrets);
        var status = GetReferenceStatus(
            resource,
            identityBinding,
            applicationResourceId,
            SecretsVaultResourceOperationPermissions.ReadSecrets,
            grantEvaluator);

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
        string applicationResourceId,
        string requiredPermission,
        ResourcePermissionGrantEvaluator? grantEvaluator)
    {
        if (resource is null)
        {
            return ("Unavailable", "warning");
        }

        if (identityBinding is null)
        {
            return ("Reference", "info");
        }

        if (grantEvaluator is null)
        {
            return ("Grant status unknown", "info");
        }

        var evaluation = grantEvaluator.Evaluate(
            ResourceIdentityReference.ForResource(applicationResourceId, identityBinding.Name),
            resource.Id,
            requiredPermission);
        if (evaluation.IsAllowed)
        {
            return ("Granted", "ok");
        }

        return ("Grant required", "warning");
    }

    private static string FormatReferenceTarget(
        string resourceId,
        string itemName,
        Resource? resource) =>
        $"{(resource is null ? resourceId : GetResourceLabel(resource))} / {itemName}";

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

    private static string GetResourceLabel(Resource resource) =>
        resource.EffectiveDisplayName;

}

public sealed record ApplicationSettingDisplayRow(
    string Name,
    string Source,
    string Target,
    string? Detail,
    string Status,
    string StatusKind,
    bool IsSensitiveLiteral = false)
{
    public string StatusCssClass => $"reference-status reference-status-{StatusKind}";
}
