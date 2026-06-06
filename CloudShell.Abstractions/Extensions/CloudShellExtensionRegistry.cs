using System.Reflection;

namespace CloudShell.Abstractions.Extensions;

public sealed class CloudShellExtensionRegistry
{
    private readonly List<CloudShellExtensionRegistration> _extensions = [];

    public IReadOnlyList<CloudShellExtensionRegistration> Extensions => _extensions;

    public IReadOnlyList<Assembly> ViewAssemblies => _extensions
        .SelectMany(extension => extension.Views
            .Select(view => view.ComponentType.Assembly)
            .Concat(extension.CustomViews
                .SelectMany(view => view.ViewMenuItems)
                .Select(menuItem => menuItem.ComponentType.Assembly))
            .Concat(extension.ResourceTypes.Select(type => type.RegistrationComponentType.Assembly)))
        .Distinct()
        .ToArray();

    internal void Add(CloudShellExtensionRegistration extension)
    {
        ValidateManifest(extension.Manifest);

        if (_extensions.Any(existing => string.Equals(existing.Id, extension.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"An extension with id '{extension.Id}' is already registered.");
        }

        _extensions.Add(extension);
    }

    public void Validate()
    {
        var duplicateRoute = _extensions
            .SelectMany(extension => extension.Views
                .Select(view => new { extension.Id, view.Route })
                .Concat(extension.CustomViews.Select(view => new { extension.Id, view.Route })))
            .GroupBy(item => item.Route, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateRoute is not null)
        {
            var owners = string.Join(", ", duplicateRoute.Select(item => item.Id));
            throw new InvalidOperationException(
                $"The route '{duplicateRoute.Key}' is contributed by multiple extensions: {owners}.");
        }

        var startRouteOwners = _extensions
            .Where(extension => !string.IsNullOrWhiteSpace(extension.StartRoute))
            .ToArray();

        if (startRouteOwners.Length > 1)
        {
            var owners = string.Join(", ", startRouteOwners.Select(extension => extension.Id));
            throw new InvalidOperationException(
                $"Multiple extensions configure the shell start route: {owners}.");
        }

        if (startRouteOwners.Length == 1)
        {
            var startRoute = startRouteOwners[0].StartRoute!;
            var knownRoutes = _extensions
                .SelectMany(extension => extension.Views.Select(view => view.Route)
                    .Concat(extension.CustomViews.Select(view => view.Route))
                    .Concat(extension.NavigationItems.Select(item => item.Href)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!knownRoutes.Contains(startRoute))
            {
                throw new InvalidOperationException(
                    $"The shell start route '{startRoute}' is not contributed by any installed extension.");
            }
        }

        var duplicateCustomView = _extensions
            .SelectMany(extension => extension.CustomViews.Select(view => new { extension.Id, View = view }))
            .GroupBy(item => item.View.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateCustomView is not null)
        {
            var owners = string.Join(", ", duplicateCustomView.Select(item => item.Id));
            throw new InvalidOperationException(
                $"The custom shell view '{duplicateCustomView.Key}' is contributed by multiple extensions: {owners}.");
        }

        var duplicateCustomViewMenuItem = _extensions
            .SelectMany(extension => extension.CustomViews
                .SelectMany(view => view.ViewMenuItems.Select(menuItem => new { extension.Id, View = view, MenuItem = menuItem })))
            .GroupBy(item => $"{item.View.Id}/{item.MenuItem.Id}", StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateCustomViewMenuItem is not null)
        {
            var owners = string.Join(", ", duplicateCustomViewMenuItem.Select(item => item.Id));
            throw new InvalidOperationException(
                $"The custom shell view menu item '{duplicateCustomViewMenuItem.Key}' is contributed by multiple extensions: {owners}.");
        }

        var duplicateResourceType = _extensions
            .SelectMany(extension => extension.ResourceTypes.Select(type => new { extension.Id, ResourceType = type }))
            .GroupBy(item => item.ResourceType.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateResourceType is not null)
        {
            var owners = string.Join(", ", duplicateResourceType.Select(item => item.Id));
            throw new InvalidOperationException(
                $"The resource type '{duplicateResourceType.Key}' is contributed by multiple extensions: {owners}.");
        }

        var providedCapabilities = _extensions
            .SelectMany(extension => extension.Provides)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingCapabilities = _extensions
            .SelectMany(extension => extension.Consumes.Select(capability => new { extension.Id, Capability = capability }))
            .Where(requirement => !providedCapabilities.Contains(requirement.Capability))
            .ToArray();

        if (missingCapabilities.Length > 0)
        {
            var requirements = string.Join(
                ", ",
                missingCapabilities.Select(requirement => $"{requirement.Id} requires {requirement.Capability}"));

            throw new InvalidOperationException($"CloudShell extension dependencies are not satisfied: {requirements}.");
        }
    }

    private static void ValidateManifest(CloudShellExtensionManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Version);
    }
}
