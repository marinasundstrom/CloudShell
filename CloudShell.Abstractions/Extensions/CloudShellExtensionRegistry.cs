using System.Reflection;

namespace CloudShell.Abstractions.Extensions;

public sealed class CloudShellExtensionRegistry
{
    private readonly List<CloudShellExtensionRegistration> _extensions = [];

    public IReadOnlyList<CloudShellExtensionRegistration> Extensions => _extensions;

    public IReadOnlyList<Assembly> ViewAssemblies => _extensions
        .SelectMany(extension => extension.Views
            .Select(view => view.ComponentType.Assembly)
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
            .SelectMany(extension => extension.Views.Select(view => new { extension.Id, View = view }))
            .GroupBy(item => item.View.Route, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateRoute is not null)
        {
            var owners = string.Join(", ", duplicateRoute.Select(item => item.Id));
            throw new InvalidOperationException(
                $"The route '{duplicateRoute.Key}' is contributed by multiple extensions: {owners}.");
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
