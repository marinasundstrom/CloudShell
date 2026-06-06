using System.Reflection;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Shell;

namespace CloudShell.Host.Shell;

public sealed class ShellCatalog(CloudShellExtensionRegistry extensionRegistry)
{
    public IReadOnlyList<CloudShellExtensionRegistration> Extensions { get; } = extensionRegistry.Extensions
        .OrderBy(extension => extension.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<NavItemContribution> NavigationItems => Extensions
        .SelectMany(extension => extension.NavigationItems.Concat(extension.Views
            .Where(view => view.ShowInNavigation)
            .Select(view => new NavItemContribution(view.Title, view.Route, view.Icon, view.Order, view.Group))))
        .OrderBy(item => item.Order)
        .ThenBy(item => item.Text, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<ShellViewContribution> Views => Extensions
        .SelectMany(extension => extension.Views)
        .OrderBy(view => view.Order)
        .ThenBy(view => view.Title, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<ResourceTypeContribution> ResourceTypes => Extensions
        .SelectMany(extension => extension.ResourceTypes)
        .OrderBy(type => type.Order)
        .ThenBy(type => type.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<Assembly> ViewAssemblies => Views
        .Select(view => view.ComponentType.Assembly)
        .Distinct()
        .ToArray();
}
