using CloudShell.Abstractions.Shell;

namespace CloudShell.Hosting.ResourceManager;

internal static class ResourceActionPreferences
{
    public static async Task<bool> StartDependenciesAutomaticallyAsync(ICloudShellUserSettingsProvider settings)
    {
        var setting = await settings.GetSettingAsync(CloudShellUserSettingKeys.ResourceManagerStartDependenciesAutomatically);
        return !bool.TryParse(setting?.Value, out var enabled) || enabled;
    }
}
