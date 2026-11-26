using CloudShell.Abstractions.Shell;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceActionPreferencesTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("invalid", true)]
    public async Task AutomaticDependencyStartupDefaultsOnAndHonorsOptOut(string? value, bool expected)
    {
        Assert.Equal(expected, await ResourceActionPreferences.StartDependenciesAutomaticallyAsync(new Settings(value)));
    }

    private sealed class Settings(string? value) : ICloudShellUserSettingsProvider
    {
        public Task<CloudShellUserSetting?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
        {
            Assert.Equal(CloudShellUserSettingKeys.ResourceManagerStartDependenciesAutomatically, key);
            return Task.FromResult(value is null ? null : new CloudShellUserSetting(key, value, DateTimeOffset.UtcNow));
        }
        public Task<IReadOnlyDictionary<string, CloudShellUserSetting>> GetSettingsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveSettingAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
