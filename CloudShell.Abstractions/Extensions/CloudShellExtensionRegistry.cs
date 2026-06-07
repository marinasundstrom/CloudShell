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

    public IReadOnlyList<CloudShellExtensionRegistration> GetActiveExtensions(
        ICloudShellExtensionActivationStore activationStore) =>
        GetStatuses(activationStore)
            .Where(status => status.IsActive)
            .Select(status => status.Extension)
            .ToArray();

    public IReadOnlyList<CloudShellExtensionStatus> GetStatuses(
        ICloudShellExtensionActivationStore activationStore)
    {
        var activationStates = activationStore.GetActivationStates();
        var statuses = _extensions.ToDictionary(
            extension => extension.Id,
            extension => GetBaseStatus(extension, activationStates),
            StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var activeExtensions = statuses
                .Where(status => status.Value.IsActive)
                .Select(status => status.Value.Extension)
                .ToArray();
            var providedCapabilities = activeExtensions
                .SelectMany(extension => extension.Provides)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var blockedExtensions = activeExtensions
                .Select(extension => new
                {
                    Extension = extension,
                    MissingCapabilities = extension.Consumes
                        .Where(capability => !providedCapabilities.Contains(capability))
                        .ToArray()
                })
                .Where(item => item.MissingCapabilities.Length > 0)
                .ToArray();

            if (blockedExtensions.Length == 0)
            {
                break;
            }

            foreach (var blockedExtension in blockedExtensions)
            {
                statuses[blockedExtension.Extension.Id] = new CloudShellExtensionStatus(
                    blockedExtension.Extension,
                    CloudShellExtensionStatusKind.Blocked,
                    $"Missing capabilities: {string.Join(", ", blockedExtension.MissingCapabilities)}");
            }
        }

        return _extensions
            .Select(extension => statuses[extension.Id])
            .ToArray();
    }

    internal void Add(CloudShellExtensionRegistration extension)
    {
        ValidateManifest(extension.Manifest);

        if (_extensions.Any(existing => string.Equals(existing.Id, extension.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"An extension with id '{extension.Id}' is already registered.");
        }

        _extensions.Add(extension);
    }

    public void Validate() =>
        Validate(new InMemoryCloudShellExtensionActivationStore());

    public void Validate(ICloudShellExtensionActivationStore activationStore)
    {
        var statuses = GetStatuses(activationStore);
        var blockedExtensions = statuses
            .Where(status => status.Kind == CloudShellExtensionStatusKind.Blocked)
            .ToArray();

        if (blockedExtensions.Length > 0)
        {
            var requirements = string.Join(
                ", ",
                blockedExtensions.Select(status => $"{status.Extension.Id} {status.Reason}"));

            throw new InvalidOperationException($"CloudShell extension dependencies are not satisfied: {requirements}.");
        }

        var activeExtensions = statuses
            .Where(status => status.IsActive)
            .Select(status => status.Extension)
            .ToArray();

        ValidateNavigationItems(activeExtensions);

        var duplicateView = activeExtensions
            .SelectMany(extension => extension.Views.Select(view => new { extension.Id, View = view }))
            .GroupBy(item => item.View.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateView is not null)
        {
            var owners = string.Join(", ", duplicateView.Select(item => item.Id));
            throw new InvalidOperationException(
                $"The view '{duplicateView.Key}' is contributed by multiple extensions: {owners}.");
        }

        var duplicateRoute = activeExtensions
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

        var startRouteOwners = activeExtensions
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
            var knownRoutes = activeExtensions
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

        var duplicateCustomView = activeExtensions
            .SelectMany(extension => extension.CustomViews.Select(view => new { extension.Id, View = view }))
            .GroupBy(item => item.View.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateCustomView is not null)
        {
            var owners = string.Join(", ", duplicateCustomView.Select(item => item.Id));
            throw new InvalidOperationException(
                $"The shell-hosted view '{duplicateCustomView.Key}' is contributed by multiple extensions: {owners}.");
        }

        var duplicateCustomViewMenuItem = activeExtensions
            .SelectMany(extension => extension.CustomViews
                .SelectMany(view => view.ViewMenuItems.Select(menuItem => new { extension.Id, View = view, MenuItem = menuItem })))
            .GroupBy(item => $"{item.View.Id}/{item.MenuItem.Id}", StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateCustomViewMenuItem is not null)
        {
            var owners = string.Join(", ", duplicateCustomViewMenuItem.Select(item => item.Id));
            throw new InvalidOperationException(
                $"The shell-hosted view menu item '{duplicateCustomViewMenuItem.Key}' is contributed by multiple extensions: {owners}.");
        }

        var duplicateResourceType = activeExtensions
            .SelectMany(extension => extension.ResourceTypes.Select(type => new { extension.Id, ResourceType = type }))
            .GroupBy(item => item.ResourceType.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateResourceType is not null)
        {
            var owners = string.Join(", ", duplicateResourceType.Select(item => item.Id));
            throw new InvalidOperationException(
                $"The resource type '{duplicateResourceType.Key}' is contributed by multiple extensions: {owners}.");
        }

    }

    private static void ValidateNavigationItems(
        IReadOnlyList<CloudShellExtensionRegistration> activeExtensions)
    {
        var knownViewIds = activeExtensions
            .SelectMany(extension => extension.Views.Select(view => view.Id)
                .Concat(extension.CustomViews.Select(view => view.Id)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownTarget = activeExtensions
            .SelectMany(extension => extension.NavigationItems.Select(item => new { extension.Id, Item = item }))
            .FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(item.Item.Target.ViewId) &&
                !knownViewIds.Contains(item.Item.Target.ViewId));

        if (unknownTarget is not null)
        {
            throw new InvalidOperationException(
                $"The navigation item '{unknownTarget.Item.Id}' targets unknown view '{unknownTarget.Item.Target.ViewId}'.");
        }

        var duplicateNavigationItem = activeExtensions
            .SelectMany(extension => extension.NavigationItems.Select(item => new { extension.Id, Item = item }))
            .GroupBy(item => item.Item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Id = group.Key,
                Items = group.ToArray(),
                Replacements = group.Where(item => item.Item.ReplacesExisting).ToArray()
            })
            .FirstOrDefault(group =>
                group.Replacements.Length > 1 ||
                (group.Items.Length > 1 && group.Replacements.Length == 0) ||
                (group.Items.Length == 1 && group.Replacements.Length == 1));

        if (duplicateNavigationItem is null)
        {
            return;
        }

        var owners = string.Join(", ", duplicateNavigationItem.Items.Select(item => item.Id));
        if (duplicateNavigationItem.Replacements.Length > 1)
        {
            throw new InvalidOperationException(
                $"The navigation item '{duplicateNavigationItem.Id}' is replaced by multiple extensions: {owners}.");
        }

        if (duplicateNavigationItem.Replacements.Length == 1 &&
            duplicateNavigationItem.Items.Length == 1)
        {
            throw new InvalidOperationException(
                $"The navigation item '{duplicateNavigationItem.Id}' cannot be replaced because no active extension contributes it.");
        }

        throw new InvalidOperationException(
            $"The navigation item '{duplicateNavigationItem.Id}' is contributed by multiple extensions: {owners}.");
    }

    private static CloudShellExtensionStatus GetBaseStatus(
        CloudShellExtensionRegistration extension,
        IReadOnlyDictionary<string, CloudShellExtensionActivationState> activationStates)
    {
        if (extension.ActivationPolicy == CloudShellExtensionActivationPolicy.Enabled)
        {
            return new CloudShellExtensionStatus(
                extension,
                CloudShellExtensionStatusKind.EnabledByHost,
                "Enabled by host configuration.");
        }

        if (extension.ActivationPolicy == CloudShellExtensionActivationPolicy.Disabled)
        {
            return new CloudShellExtensionStatus(
                extension,
                CloudShellExtensionStatusKind.DisabledByHost,
                "Disabled by host configuration.");
        }

        var activationState = activationStates.GetValueOrDefault(
            extension.Id,
            CloudShellExtensionActivationState.Disabled);

        return new CloudShellExtensionStatus(
            extension,
            activationState == CloudShellExtensionActivationState.Enabled
                ? CloudShellExtensionStatusKind.Enabled
                : CloudShellExtensionStatusKind.Disabled);
    }

    private static void ValidateManifest(CloudShellExtensionManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Version);
    }
}
