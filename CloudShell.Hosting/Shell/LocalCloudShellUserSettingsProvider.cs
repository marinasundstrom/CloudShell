using System.Security.Claims;
using System.Text.Json;
using CloudShell.Abstractions.Shell;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Hosting;

namespace CloudShell.Hosting.Shell;

internal sealed class LocalCloudShellUserSettingsProvider(
    AuthenticationStateProvider authenticationStateProvider,
    IHostEnvironment environment) : ICloudShellLocalUserSettingsProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string settingsPath = Path.GetFullPath(
        "Data/environment-settings.json",
        environment.ContentRootPath);

    public async Task<IReadOnlyDictionary<string, CloudShellUserSetting>> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        var userKey = await GetUserKeyAsync();
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadAsync(cancellationToken);
            return document.Users.GetValueOrDefault(userKey, [])
                .Select(item => new CloudShellUserSetting(item.Key, item.Value.Value, item.Value.UpdatedAt))
                .ToDictionary(setting => setting.Key, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<CloudShellUserSetting?> GetSettingAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        key = NormalizeKey(key);
        var userKey = await GetUserKeyAsync();
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadAsync(cancellationToken);
            return document.Users.GetValueOrDefault(userKey, [])
                .TryGetValue(key, out var setting)
                ? new CloudShellUserSetting(key, setting.Value, setting.UpdatedAt)
                : null;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task SetSettingAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        key = NormalizeKey(key);
        var userKey = await GetUserKeyAsync();
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadAsync(cancellationToken);
            if (!document.Users.TryGetValue(userKey, out var settings))
            {
                settings = new Dictionary<string, StoredSetting>(StringComparer.OrdinalIgnoreCase);
                document.Users[userKey] = settings;
            }

            settings[key] = new StoredSetting(value, DateTimeOffset.UtcNow);
            await PersistAsync(document, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task RemoveSettingAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        key = NormalizeKey(key);
        var userKey = await GetUserKeyAsync();
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadAsync(cancellationToken);
            if (document.Users.TryGetValue(userKey, out var settings) &&
                settings.Remove(key))
            {
                await PersistAsync(document, cancellationToken);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<string> GetUserKeyAsync()
    {
        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var value =
                user.FindFirstValue(ClaimTypes.NameIdentifier) ??
                user.FindFirstValue("sub") ??
                user.Identity.Name;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return $"user:{value.Trim()}";
            }
        }

        return "local";
    }

    private async Task<SettingsDocument> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(settingsPath))
        {
            return new SettingsDocument();
        }

        await using var stream = new FileStream(
            settingsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        var document = await JsonSerializer.DeserializeAsync<SettingsDocument>(
            stream,
            SerializerOptions,
            cancellationToken);

        return document ?? new SettingsDocument();
    }

    private async Task PersistAsync(
        SettingsDocument document,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var temporaryPath = $"{settingsPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken);
            }

            File.Move(temporaryPath, settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string NormalizeKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.Trim();
    }

    private sealed class SettingsDocument
    {
        public Dictionary<string, Dictionary<string, StoredSetting>> Users { get; init; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record StoredSetting(string Value, DateTimeOffset UpdatedAt);
}
