using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Shell;
using Microsoft.Extensions.Options;

namespace CloudShell.Hosting.ResourceManager;

internal sealed class ResourceManagerDisplaySettings(
    IOptionsMonitor<ResourceManagerUiOptions> options,
    ICloudShellAuthorizationService authorization,
    ICloudShellUserSettingsProvider userSettings)
{
    public async Task<ResourceManagerDisplaySelection> GetAsync(CancellationToken cancellationToken = default)
    {
        var configured = options.CurrentValue;
        var canShowRuntimeManagedResources = authorization.HasPermission(
            CloudShellPermissions.Resources.ReadRuntimeManaged);
        var showRuntimeManagedResources = await GetBooleanSettingAsync(
            CloudShellUserSettingKeys.ResourceManagerShowRuntimeManagedResources,
            configured.ShowRuntimeManagedResources,
            cancellationToken);
        var showHiddenResources = await GetBooleanSettingAsync(
            CloudShellUserSettingKeys.ResourceManagerShowHiddenResources,
            configured.ShowHiddenResources,
            cancellationToken);
        var enableDisplayNames = await GetBooleanSettingAsync(
            CloudShellUserSettingKeys.ResourceManagerEnableDisplayNames,
            configured.EnableDisplayNames,
            cancellationToken);

        return new ResourceManagerDisplaySelection(
            enableDisplayNames,
            configured.EnableDisplayNames,
            canShowRuntimeManagedResources && showRuntimeManagedResources,
            configured.ShowRuntimeManagedResources,
            canShowRuntimeManagedResources,
            showHiddenResources,
            configured.ShowHiddenResources);
    }

    public async Task SelectAsync(
        bool enableDisplayNames,
        bool showRuntimeManagedResources,
        bool showHiddenResources,
        CancellationToken cancellationToken = default)
    {
        await userSettings.SetSettingAsync(
            CloudShellUserSettingKeys.ResourceManagerEnableDisplayNames,
            enableDisplayNames ? "true" : "false",
            cancellationToken);

        if (authorization.HasPermission(CloudShellPermissions.Resources.ReadRuntimeManaged))
        {
            await userSettings.SetSettingAsync(
                CloudShellUserSettingKeys.ResourceManagerShowRuntimeManagedResources,
                showRuntimeManagedResources ? "true" : "false",
                cancellationToken);
        }

        await userSettings.SetSettingAsync(
            CloudShellUserSettingKeys.ResourceManagerShowHiddenResources,
            showHiddenResources ? "true" : "false",
            cancellationToken);
    }

    private async Task<bool> GetBooleanSettingAsync(
        string key,
        bool defaultValue,
        CancellationToken cancellationToken)
    {
        var setting = await userSettings.GetSettingAsync(key, cancellationToken);
        return bool.TryParse(setting?.Value, out var value)
            ? value
            : defaultValue;
    }
}

internal sealed record ResourceManagerDisplaySelection(
    bool EnableDisplayNames,
    bool DefaultEnableDisplayNames,
    bool ShowRuntimeManagedResources,
    bool DefaultShowRuntimeManagedResources,
    bool CanShowRuntimeManagedResources,
    bool ShowHiddenResources,
    bool DefaultShowHiddenResources)
{
    public static ResourceManagerDisplaySelection Default { get; } = new(
        true,
        true,
        false,
        false,
        false,
        false,
        false);

    public bool ShowsResource(Resource resource)
    {
        if (resource.Visibility == ResourceVisibility.Normal)
        {
            return true;
        }

        if (!ShowHiddenResources)
        {
            return false;
        }

        return resource.ManagementMode != ResourceManagementMode.RuntimeManaged ||
            ShowRuntimeManagedResources;
    }

    public string GetResourceLabel(Resource resource) =>
        EnableDisplayNames ? resource.EffectiveDisplayName : resource.Id;

    public string GetResourceSortLabel(Resource resource) =>
        EnableDisplayNames ? resource.EffectiveDisplayName : resource.Id;

    public string GetResourceName(Resource resource) =>
        string.IsNullOrWhiteSpace(resource.Name)
            ? ResourceId.TryParse(resource.Id, out var resourceId)
                ? resourceId.Name
                : resource.Id
            : resource.Name;

    public bool ShouldShowDisplayName(Resource resource) =>
        EnableDisplayNames &&
        !string.IsNullOrWhiteSpace(resource.DisplayName) &&
        !string.Equals(resource.DisplayName, GetResourceName(resource), StringComparison.Ordinal);
}
