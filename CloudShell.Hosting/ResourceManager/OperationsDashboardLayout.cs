using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CloudShell.Hosting.ResourceManager;

internal sealed record OperationsDashboardLayout(int Version, string[] Widgets)
{
    public static void Reorder(List<string> widgets, string source, string target, bool after)
    {
        if (source == target || !widgets.Contains(source) || !widgets.Contains(target)) return;
        widgets.Remove(source);
        widgets.Insert(widgets.IndexOf(target) + (after ? 1 : 0), source);
    }

    public static string SettingKey(string environment, string? resourceId) =>
        "operations.dashboard." + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(environment + "\n" + (resourceId is null ? "system" : "resource:" + resourceId))));

    public static OperationsDashboardLayout Parse(string? json, IEnumerable<string> defaults)
    {
        if (json is null) return new(1, defaults.ToArray());
        var layout = JsonSerializer.Deserialize<OperationsDashboardLayout>(json);
        if (layout is null || layout.Version != 1 || layout.Widgets is null || layout.Widgets.Length > 100 ||
            layout.Widgets.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 256) ||
            layout.Widgets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != layout.Widgets.Length)
            throw new JsonException("Unsupported dashboard layout.");
        return layout;
    }
}
