using CloudShell.Abstractions.Shell;
using Microsoft.Extensions.Options;

namespace CloudShell.Hosting.Shell;

internal sealed class ConfiguredCloudShellUserSettingsProvider(
    ICloudShellLocalUserSettingsProvider localSettings,
    IEnumerable<ICloudShellControlPlaneUserSettingsProvider> controlPlaneSettings,
    IOptions<CloudShellUserSettingsOptions> options) : ICloudShellUserSettingsProvider
{
    public Task<IReadOnlyDictionary<string, CloudShellUserSetting>> GetSettingsAsync(
        CancellationToken cancellationToken = default) =>
        SelectProvider().GetSettingsAsync(cancellationToken);

    public Task<CloudShellUserSetting?> GetSettingAsync(
        string key,
        CancellationToken cancellationToken = default) =>
        SelectProvider().GetSettingAsync(key, cancellationToken);

    public Task SetSettingAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default) =>
        SelectProvider().SetSettingAsync(key, value, cancellationToken);

    public Task RemoveSettingAsync(
        string key,
        CancellationToken cancellationToken = default) =>
        SelectProvider().RemoveSettingAsync(key, cancellationToken);

    private ICloudShellUserSettingsProvider SelectProvider()
    {
        if (options.Value.Storage == CloudShellUserSettingsStorage.Local)
        {
            return localSettings;
        }

        return controlPlaneSettings.LastOrDefault() ??
            throw new InvalidOperationException(
                "Shell user settings are configured for ControlPlane storage, but no Control Plane settings provider is registered.");
    }
}
