using CloudShell.Abstractions.Shell;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Extensions;

public sealed class CloudShellExtensionRegistration(
    CloudShellExtensionManifest manifest,
    CloudShellExtensionActivationPolicy activationPolicy,
    IReadOnlyList<NavItemContribution> navigationItems,
    IReadOnlyList<ShellViewContribution> views,
    IReadOnlyList<ResourceTypeContribution> resourceTypes,
    IReadOnlyList<Type> resourceProviderTypes,
    IReadOnlyList<Type> logProviderTypes,
    IReadOnlyList<CustomShellViewContribution>? customViews = null,
    string? startRoute = null)
{
    public CloudShellExtensionManifest Manifest { get; } = manifest;

    public string Id => Manifest.Id;

    public string DisplayName => Manifest.DisplayName;

    public string Description => Manifest.Description;

    public string Version => Manifest.Version;

    public IReadOnlyList<string> Provides => Manifest.Provides;

    public IReadOnlyList<string> Consumes => Manifest.Consumes;

    public CloudShellExtensionActivationPolicy ActivationPolicy { get; } = activationPolicy;

    public IReadOnlyList<NavItemContribution> NavigationItems { get; } = navigationItems;

    public IReadOnlyList<ShellViewContribution> Views { get; } = views;

    public IReadOnlyList<ResourceTypeContribution> ResourceTypes { get; } = resourceTypes;

    public IReadOnlyList<Type> ResourceProviderTypes { get; } = resourceProviderTypes;

    public IReadOnlyList<Type> LogProviderTypes { get; } = logProviderTypes;

    public IReadOnlyList<CustomShellViewContribution> CustomViews { get; } = customViews ?? [];

    public string? StartRoute { get; } = startRoute;
}
