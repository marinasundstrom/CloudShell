using System.Reflection;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Shell;

namespace CloudShell.Hosting.Shell;

public sealed class ShellCatalog(
    CloudShellExtensionRegistry extensionRegistry,
    ICloudShellExtensionActivationStore activationStore)
{
    public IReadOnlyList<CloudShellExtensionRegistration> Extensions => extensionRegistry
        .GetActiveExtensions(activationStore)
        .OrderBy(extension => extension.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<CloudShellExtensionRegistration> SupportedExtensions => extensionRegistry.Extensions
        .OrderBy(extension => extension.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<CloudShellExtensionStatus> ExtensionStatuses => extensionRegistry
        .GetStatuses(activationStore)
        .OrderBy(status => status.Extension.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<NavItemContribution> NavigationItems
    {
        get
        {
            var replacementNavigationItemIds = GetReplacementNavigationItemIds();

            return Extensions
                .SelectMany(extension => GetEffectiveNavigationItems(extension, replacementNavigationItemIds)
                    .Concat(extension.CustomViews
                        .Where(view => view.ShowInNavigation)
                        .Select(view => new NavItemContribution(
                            CreateGeneratedNavigationId(view.Route),
                            view.Title,
                            view.Route,
                            NavItemTarget.ForHref(view.Route),
                            view.Icon,
                            view.Order,
                            view.Group))))
                .OrderBy(item => item.Order)
                .ThenBy(item => item.Text, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public IReadOnlyList<ShellViewContribution> Views => Extensions
        .SelectMany(extension => extension.Views)
        .OrderBy(view => view.Id, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<CustomShellViewContribution> CustomViews => Extensions
        .SelectMany(extension => extension.CustomViews)
        .OrderBy(view => view.Order)
        .ThenBy(view => view.Title, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<ResourceTypeContribution> ResourceTypes => Extensions
        .SelectMany(extension => extension.ResourceTypes)
        .OrderBy(type => type.Order)
        .ThenBy(type => type.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<Assembly> ViewAssemblies => SupportedExtensions
        .SelectMany(extension => extension.Views
            .Select(view => view.ComponentType.Assembly)
            .Concat(extension.CustomViews
                .SelectMany(view => view.ViewMenuItems)
                .Select(menuItem => menuItem.ComponentType.Assembly))
            .Concat(extension.ResourceTypes.Select(type => type.RegistrationComponentType.Assembly)))
        .Distinct()
        .ToArray();

    public string? StartRoute => Extensions
        .Select(extension => extension.StartRoute)
        .FirstOrDefault(route => !string.IsNullOrWhiteSpace(route));

    public bool IsActiveRouteComponent(Type componentType) =>
        Extensions
            .SelectMany(extension => extension.Views.Select(view => view.ComponentType)
                .Concat(extension.CustomViews
                    .SelectMany(view => view.ViewMenuItems)
                    .Select(menuItem => menuItem.ComponentType)))
            .Any(type => type == componentType);

    public ShellViewContribution? GetView(string viewId) =>
        Extensions
            .SelectMany(extension => extension.Views)
            .FirstOrDefault(view => string.Equals(view.Id, viewId, StringComparison.OrdinalIgnoreCase))
        ?? GetCustomViewAsShellView(viewId);

    public ShellViewContribution? GetView(Type viewType) =>
        Extensions
            .SelectMany(extension => extension.Views)
            .FirstOrDefault(view => view.ComponentType == viewType);

    public string? GetViewRoute(string viewId) =>
        GetView(viewId)?.Route;

    private ShellViewContribution? GetCustomViewAsShellView(string viewId)
    {
        var customView = Extensions
            .SelectMany(extension => extension.CustomViews)
            .FirstOrDefault(view => string.Equals(view.Id, viewId, StringComparison.OrdinalIgnoreCase));

        return customView is null
            ? null
            : new ShellViewContribution(customView.Id, customView.Route, typeof(Components.Pages.Shell.CustomShellView), []);
    }

    private IReadOnlySet<string> GetReplacementNavigationItemIds() =>
        Extensions
            .SelectMany(extension => extension.NavigationItems)
            .Where(item => item.ReplacesExisting)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<NavItemContribution> GetEffectiveNavigationItems(
        CloudShellExtensionRegistration extension,
        IReadOnlySet<string> replacementNavigationItemIds) =>
        extension.NavigationItems
            .Where(item => item.ShowInNavigation)
            .Where(item => item.ReplacesExisting || !replacementNavigationItemIds.Contains(item.Id));

    private static string CreateGeneratedNavigationId(string route) =>
        $"route:{route}";
}
