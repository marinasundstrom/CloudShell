namespace CloudShell.Abstractions.Extensions;

public sealed record CloudShellExtensionManifest(
    string Id,
    string DisplayName,
    string Description,
    string Version,
    IReadOnlyList<string> Provides,
    IReadOnlyList<string> Consumes);
