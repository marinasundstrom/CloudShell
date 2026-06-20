namespace CloudShell.Hosting.Components.Layout;

public sealed record CloudShellTabbedLayoutItem(
    string Id,
    string Title,
    int Order,
    string Group = "General",
    string? GroupTitle = null,
    string? Icon = null,
    string? Description = null,
    string? ModuleId = null,
    string? Href = null);
