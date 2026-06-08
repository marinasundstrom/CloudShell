namespace CloudShell.Abstractions.Shell;

public interface ICloudShellUserSettingsProvider
{
    Task<IReadOnlyDictionary<string, CloudShellUserSetting>> GetSettingsAsync(
        CancellationToken cancellationToken = default);

    Task<CloudShellUserSetting?> GetSettingAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task SetSettingAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default);

    Task RemoveSettingAsync(
        string key,
        CancellationToken cancellationToken = default);
}

public interface ICloudShellLocalUserSettingsProvider : ICloudShellUserSettingsProvider;

public interface ICloudShellControlPlaneUserSettingsProvider : ICloudShellUserSettingsProvider;

public enum CloudShellUserSettingsStorage
{
    Local,
    ControlPlane
}

public sealed class CloudShellUserSettingsOptions
{
    public const string SectionName = "Shell:EnvironmentSettings";

    public CloudShellUserSettingsStorage Storage { get; set; } =
        CloudShellUserSettingsStorage.Local;
}

public sealed record CloudShellUserSetting(
    string Key,
    string Value,
    DateTimeOffset UpdatedAt);

public static class CloudShellUserSettingKeys
{
    public const string ThemeMode = "shell.theme";
    public const string NavigationCollapsed = "shell.navigation.collapsed";
}
