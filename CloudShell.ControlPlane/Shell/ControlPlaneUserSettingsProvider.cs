using System.Security.Claims;
using System.Text.Json;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Shell;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace CloudShell.ControlPlane.Shell;

public sealed class ControlPlaneUserSettingsProvider(
    IHttpContextAccessor httpContextAccessor,
    ICloudShellAuthorizationService authorization,
    IHostEnvironment environment) : ICloudShellControlPlaneUserSettingsProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string settingsPath = Path.GetFullPath(
        "Data/environment-settings.json",
        environment.ContentRootPath);

    public async Task<IReadOnlyDictionary<string, CloudShellUserSetting>> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        var userKey = GetUserKey();
        await gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadAsync(cancellationToken);
            return document.Users.GetValueOrDefault(userKey, [])
                .Select(item => new CloudShellUserSetting(item.Key, item.Value.Value, item.Value.UpdatedAt))
                .ToDictionary(setting => setting.Key, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<CloudShellUserSetting?> GetSettingAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        key = NormalizeKey(key);
        var userKey = GetUserKey();
        await gate.WaitAsync(cancellationToken);
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
            gate.Release();
        }
    }

    public async Task SetSettingAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        key = NormalizeKey(key);
        var userKey = GetUserKey();
        await gate.WaitAsync(cancellationToken);
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
            gate.Release();
        }
    }

    public async Task RemoveSettingAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        key = NormalizeKey(key);
        var userKey = GetUserKey();
        await gate.WaitAsync(cancellationToken);
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
            gate.Release();
        }
    }

    private string GetUserKey()
    {
        if (!authorization.IsAuthenticated)
        {
            throw new UnauthorizedAccessException("An authenticated user is required to access CloudShell settings.");
        }

        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
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

        await using var stream = File.OpenRead(settingsPath);
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
        await using var stream = File.Create(settingsPath);
        await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken);
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
