namespace CloudShell.Abstractions.Shell;

public sealed record NavItemContribution(
    string Id,
    string Text,
    string Href,
    NavItemTarget Target,
    string Icon,
    int Order,
    string Group = "Workspace",
    bool ReplacesExisting = false,
    bool ShowInNavigation = true)
{
    public NavItemContribution(
        string text,
        string href,
        string icon,
        int order,
        string group = "Workspace")
        : this($"navigation:{href}", text, href, NavItemTarget.ForHref(href), icon, order, group)
    {
    }
}
