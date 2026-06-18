using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.Extensions;

public interface ICloudShellExtensionBuilder
{
    IServiceCollection Services { get; }

    ICloudShellExtensionBuilder RegisterView<TComponent>();

    ICloudShellExtensionBuilder RegisterView<TComponent>(
        string id);

    ICloudShellExtensionBuilder AddNavigationItem<TView>(
        string text,
        string icon,
        int order,
        string group = "Workspace",
        string? parentId = null);

    ICloudShellExtensionBuilder AddNavigationItem<TView>(
        string id,
        string text,
        string icon,
        int order,
        string group = "Workspace",
        string? parentId = null);

    ICloudShellExtensionBuilder AddNavigationItem(
        string id,
        string text,
        NavItemTarget target,
        string icon,
        int order,
        string group = "Workspace",
        string? parentId = null);

    ICloudShellExtensionBuilder ReplaceNavigationItem<TView>(
        string id,
        string text,
        string icon,
        int order,
        string group = "Workspace",
        string? parentId = null);

    ICloudShellExtensionBuilder ReplaceNavigationItem(
        string id,
        string text,
        NavItemTarget target,
        string icon,
        int order,
        string group = "Workspace",
        string? parentId = null);

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

    ICloudShellExtensionBuilder UseStartView(string viewId);

    ICloudShellExtensionBuilder UseStartView<TView>();

    ICloudShellExtensionBuilder AddResourceProvider<TProvider>()
        where TProvider : class, IResourceProvider;

    ICloudShellExtensionBuilder AddLogProvider<TProvider>()
        where TProvider : class, ILogProvider;

    ICloudShellExtensionBuilder AddResourceType<TRegistrationComponent>(
        string id,
        string displayName,
        string description,
        string icon,
        int order,
        ResourceTypeProbeOptions? probeOptions = null,
        ResourceClass resourceClass = ResourceClass.Generic);

    ICloudShellExtensionBuilder AddResourceType<TRegistrationComponent, TUpdateComponent>(
        string id,
        string displayName,
        string description,
        string icon,
        int order,
        ResourceTypeProbeOptions? probeOptions = null,
        ResourceClass resourceClass = ResourceClass.Generic);

    ICloudShellExtensionBuilder AddResourceTypeEndpoint(
        string resourceTypeId,
        ResourceEndpointDescriptor descriptor);

    ICloudShellExtensionBuilder AddResourceTab<TComponent>(
        string resourceTypeId,
        ResourceViewId id,
        string title,
        int order,
        bool showsApplyButton = false,
        string? groupTitle = null,
        string? icon = null);

    ICloudShellExtensionBuilder AddResourcePredefinedViewSection<TComponent>(
        string resourceTypeId,
        ResourceViewId viewId,
        string id,
        string title,
        int order);

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
