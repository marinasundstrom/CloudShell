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

        return new ResourceManagerDisplaySelection(
            canShowRuntimeManagedResources && showRuntimeManagedResources,
            configured.ShowRuntimeManagedResources,
            canShowRuntimeManagedResources,
            showHiddenResources,
            configured.ShowHiddenResources);
    }

    public async Task SelectAsync(
        bool showRuntimeManagedResources,
        bool showHiddenResources,
        CancellationToken cancellationToken = default)
    {
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
    bool ShowRuntimeManagedResources,
    bool DefaultShowRuntimeManagedResources,
    bool CanShowRuntimeManagedResources,
    bool ShowHiddenResources,
    bool DefaultShowHiddenResources)
{
    public static ResourceManagerDisplaySelection Default { get; } = new(
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
}
