using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Logs;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.Extensions;

public interface ICloudShellExtensionBuilder
{
    IServiceCollection Services { get; }

    ICloudShellExtensionBuilder AddView<TComponent>(
        string title,
        string route,
        string icon,
        int order,
        string group = "Workspace",
        bool showInNavigation = true);

    ICloudShellExtensionBuilder AddNavigation(
        string text,
        string href,
        string icon,
        int order,
        string group = "Workspace");

    ICloudShellExtensionBuilder AddCustomView(
        string id,
        string title,
        string route,
        string icon,
        int order,
        string group = "Workspace",
        string? description = null,
        bool showInNavigation = true);

    ICloudShellExtensionBuilder AddCustomViewMenuItem<TComponent>(
        string viewId,
        string id,
        string title,
        int order,
        string? description = null);

    ICloudShellExtensionBuilder UseStartRoute(string route);

    ICloudShellExtensionBuilder AddResourceProvider<TProvider>()
        where TProvider : class, IResourceProvider;

    ICloudShellExtensionBuilder AddLogProvider<TProvider>()
        where TProvider : class, ILogProvider;

    ICloudShellExtensionBuilder AddResourceType<TRegistrationComponent>(
        string id,
        string displayName,
        string description,
        string icon,
        int order);

    ICloudShellExtensionBuilder AddResourceType<TRegistrationComponent, TUpdateComponent>(
        string id,
        string displayName,
        string description,
        string icon,
        int order);

    ICloudShellExtensionBuilder AddResourceTab<TComponent>(
        string resourceTypeId,
        string id,
        string title,
        int order,
        bool showsApplyButton = false);

    ICloudShellExtensionBuilder AddSingleton<TService>()
        where TService : class;

    ICloudShellExtensionBuilder AddSingleton<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService;

    ICloudShellExtensionBuilder AddScoped<TService>()
        where TService : class;

    ICloudShellExtensionBuilder AddScoped<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService;

    ICloudShellExtensionBuilder AddTransient<TService>()
        where TService : class;

    ICloudShellExtensionBuilder AddTransient<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService;
}
