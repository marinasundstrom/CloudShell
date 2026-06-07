namespace CloudShell.Abstractions.Shell;

public sealed record NavItemTarget(string? ViewId, string? Href, Type? ViewType = null)
{
    public static NavItemTarget ForView(string viewId) => new(viewId, null);

    public static NavItemTarget ForView<TComponent>() => new(null, null, typeof(TComponent));

    public static NavItemTarget ForHref(string href) => new(null, href);

    internal static string GetViewId(Type componentType) =>
        componentType.FullName ?? componentType.Name;
}
