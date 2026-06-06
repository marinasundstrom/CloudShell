using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.Extensions;

internal sealed class CloudShellExtensionBuilder(
    IServiceCollection services,
    CloudShellExtensionManifest manifest) : ICloudShellExtensionBuilder
{
    private readonly List<NavItemContribution> _navigationItems = [];
    private readonly List<ShellViewContribution> _views = [];
    private readonly List<ResourceTypeContribution> _resourceTypes = [];

    public IServiceCollection Services { get; } = services;

    public ICloudShellExtensionBuilder AddView<TComponent>(
        string title,
        string route,
        string icon,
        int order,
        string group = "Workspace",
        bool showInNavigation = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        _views.Add(new ShellViewContribution(
            title,
            NormalizeRoute(route),
            typeof(TComponent),
            icon,
            order,
            group,
            showInNavigation));

        return this;
    }

    public ICloudShellExtensionBuilder AddNavigation(
        string text,
        string href,
        string icon,
        int order,
        string group = "Workspace")
    {
        _navigationItems.Add(new NavItemContribution(text, NormalizeRoute(href), icon, order, group));
        return this;
    }

    public ICloudShellExtensionBuilder AddResourceProvider<TProvider>()
        where TProvider : class, IResourceProvider
    {
        Services.AddSingleton<TProvider>();
        Services.AddSingleton<IResourceProvider>(
            serviceProvider => serviceProvider.GetRequiredService<TProvider>());
        return this;
    }

    public ICloudShellExtensionBuilder AddResourceType<TRegistrationComponent>(
        string id,
        string displayName,
        string description,
        string icon,
        int order)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        _resourceTypes.Add(new ResourceTypeContribution(
            id,
            displayName,
            description,
            icon,
            order,
            typeof(TRegistrationComponent)));

        return this;
    }

    public ICloudShellExtensionBuilder AddSingleton<TService>()
        where TService : class
    {
        Services.AddSingleton<TService>();
        return this;
    }

    public ICloudShellExtensionBuilder AddSingleton<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        Services.AddSingleton<TService, TImplementation>();
        return this;
    }

    public ICloudShellExtensionBuilder AddScoped<TService>()
        where TService : class
    {
        Services.AddScoped<TService>();
        return this;
    }

    public ICloudShellExtensionBuilder AddScoped<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        Services.AddScoped<TService, TImplementation>();
        return this;
    }

    public ICloudShellExtensionBuilder AddTransient<TService>()
        where TService : class
    {
        Services.AddTransient<TService>();
        return this;
    }

    public ICloudShellExtensionBuilder AddTransient<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        Services.AddTransient<TService, TImplementation>();
        return this;
    }

    public CloudShellExtensionRegistration Build() =>
        new(manifest, _navigationItems.ToArray(), _views.ToArray(), _resourceTypes.ToArray());

    private static string NormalizeRoute(string route) =>
        route == "/" ? route : "/" + route.Trim('/');
}
