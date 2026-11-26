using System.Text.Json;
using CloudShell.Abstractions.Authorization;
using Microsoft.Extensions.Hosting;

namespace CloudShell.Hosting.ResourceManager;

/// <summary>Preview shared presentation preferences for users of this UI host.</summary>
public interface IOrganizationDashboardSettings
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string layout, CancellationToken cancellationToken = default);
}

internal sealed class OrganizationDashboardSettings(IHostEnvironment environment, ICloudShellAuthorizationService authorization)
    : IOrganizationDashboardSettings
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string PathName = Path.Combine(environment.ContentRootPath, "Data", "organization-dashboards.json");

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try { return (await ReadAsync(cancellationToken)).GetValueOrDefault(key); }
        finally { Gate.Release(); }
    }

    public async Task SetAsync(string key, string layout, CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CloudShellPermissions.Shell.Configure))
            throw new UnauthorizedAccessException("Organization dashboard editing requires shell.configure.");
        _ = OperationsDashboardLayout.Parse(layout, []);
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var values = await ReadAsync(cancellationToken);
            values[key] = layout;
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            var temporary = PathName + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(values), cancellationToken);
            File.Move(temporary, PathName, overwrite: true);
        }
        finally { Gate.Release(); }
    }

    private async Task<Dictionary<string, string>> ReadAsync(CancellationToken token) => !File.Exists(PathName)
        ? new(StringComparer.Ordinal)
        : JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(PathName, token))
            ?? throw new JsonException("Invalid organization dashboard settings.");
}

/// <summary>Optional host-owned widget defaults. Resource type keys are exact type IDs.</summary>
public sealed class OperationsDashboardOptions
{
    public Dictionary<string, string[]> ResourceTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string[]? System { get; set; }
}
