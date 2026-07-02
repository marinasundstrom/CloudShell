using System.Security.Claims;
using System.Text.Json;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Shell;
using CloudShell.ControlPlane.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CloudShell.ControlPlane.Shell;

public sealed class ControlPlaneUserSettingsProvider : ICloudShellControlPlaneUserSettingsProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly ICloudShellAuthorizationService authorization;
    private readonly string settingsPath;

    public ControlPlaneUserSettingsProvider(
        IHttpContextAccessor httpContextAccessor,
        ICloudShellAuthorizationService authorization,
        IHostEnvironment environment) :
        this(httpContextAccessor, authorization, new ConfigurationBuilder().Build(), environment)
    {
    }

    public ControlPlaneUserSettingsProvider(
        IHttpContextAccessor httpContextAccessor,
        ICloudShellAuthorizationService authorization,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        this.httpContextAccessor = httpContextAccessor;
        this.authorization = authorization;
        settingsPath = CloudShellDataDirectory.ResolvePath(
            "Data/environment-settings.json",
            configuration,
            environment);
    }

    public async Task<IReadOnlyDictionary<string, CloudShellUserSetting>> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        var userKey = GetUserKey();
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
        var userKey = GetUserKey();
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
        var userKey = GetUserKey();
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
        var userKey = GetUserKey();
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
