using CloudShell.Abstractions.Shell;

namespace CloudShell.Hosting.Shell;

public interface ICloudShellNavigator
{
    string GetHref<TView>(object? routeValues = null);

    string GetHref(string viewId, object? routeValues = null);

    string GetHref(NavItemTarget target, object? routeValues = null);

    void NavigateTo<TView>(object? routeValues = null, bool forceLoad = false, bool replace = false);

    void NavigateToView(string viewId, object? routeValues = null, bool forceLoad = false, bool replace = false);

    void NavigateTo(NavItemTarget target, object? routeValues = null, bool forceLoad = false, bool replace = false);

    void NavigateToHref(string href, bool forceLoad = false, bool replace = false);
}
