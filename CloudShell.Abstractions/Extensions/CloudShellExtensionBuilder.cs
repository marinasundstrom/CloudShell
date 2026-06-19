using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.Extensions;

internal sealed class CloudShellExtensionBuilder(
    IServiceCollection services,
    CloudShellExtensionManifest manifest,
    CloudShellExtensionActivationPolicy activationPolicy) : ICloudShellExtensionBuilder
{
    private readonly List<ShellViewContribution> _views = [];
    private readonly List<NavItemContribution> _navigationItems = [];
    private readonly List<CustomShellViewContribution> _customViews = [];
    private readonly List<ResourceTypeContribution> _resourceTypes = [];
    private readonly List<Type> _resourceProviderTypes = [];
    private readonly List<Type> _logProviderTypes = [];
    private string? _startRoute;

    public IServiceCollection Services { get; } = services;

    public ICloudShellExtensionBuilder RegisterView<TComponent>() =>
        RegisterView<TComponent>(ShellViewKeys.For<TComponent>());

    public ICloudShellExtensionBuilder RegisterView<TComponent>(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var componentType = typeof(TComponent);
        var route = GetPrimaryRoute(componentType);
        _views.Add(new ShellViewContribution(
            id,
            route,
            componentType,
            GetRouteParameters(route)));

        return this;
    }

    public ICloudShellExtensionBuilder AddNavigationItem<TView>(
        string text,
        string icon,
        int order,
        string group = "Workspace",
        string? parentId = null,
        IReadOnlyList<string>? requiredPermissions = null) =>
        AddNavigationItem<TView>(
            GetRegisteredView(typeof(TView)).Id,
            text,
            icon,
            order,
            group,
            parentId,
            requiredPermissions);

    public ICloudShellExtensionBuilder AddNavigationItem<TView>(
        string id,
        string text,
        string icon,
        int order,
        string group = "Workspace",
        string? parentId = null,
        IReadOnlyList<string>? requiredPermissions = null) =>
        AddNavigationItem(
            id,
            text,
            NavItemTarget.ForView<TView>(),
            icon,
            order,
            group,
            parentId,
            requiredPermissions);

    public ICloudShellExtensionBuilder AddNavigationItem(
        string id,
        string text,
        NavItemTarget target,
        string icon,
        int order,
        string group = "Workspace",
        string? parentId = null,
        IReadOnlyList<string>? requiredPermissions = null)
    {
        AddNavigationItem(
            id,
            text,
            target,
            icon,
            order,
            group,
            parentId,
            requiredPermissions,
            replacesExisting: false);

        return this;
    }

    public ICloudShellExtensionBuilder ReplaceNavigationItem<TView>(
        string id,
        string text,
        string icon,
        int order,
        string group = "Workspace",
        string? parentId = null,
        IReadOnlyList<string>? requiredPermissions = null) =>
        ReplaceNavigationItem(
            id,
            text,
            NavItemTarget.ForView<TView>(),
            icon,
            order,
            group,
            parentId,
            requiredPermissions);

    public ICloudShellExtensionBuilder ReplaceNavigationItem(
        string id,
        string text,
        NavItemTarget target,
        string icon,
        int order,
        string group = "Workspace",
        string? parentId = null,
        IReadOnlyList<string>? requiredPermissions = null)
    {
        AddNavigationItem(
            id,
            text,
            target,
            icon,
            order,
            group,
            parentId,
            requiredPermissions,
            replacesExisting: true);

        return this;
    }

    public ICloudShellExtensionBuilder AddCustomView(
        string id,
        string title,
        string route,
        string icon,
        int order,
        string group = "Workspace",
        string? description = null,
        bool showInNavigation = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        _customViews.Add(new CustomShellViewContribution(
            id,
            title,
            NormalizeRoute(route),
            icon,
            order,
            group,
            description,
            showInNavigation));

        return this;
    }

    public ICloudShellExtensionBuilder AddCustomViewMenuItem<TComponent>(
        string viewId,
        string id,
        string title,
        int order,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewId);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var viewIndex = _customViews.FindIndex(view =>
            string.Equals(view.Id, viewId, StringComparison.OrdinalIgnoreCase));
        if (viewIndex < 0)
        {
            throw new InvalidOperationException(
                $"Custom shell view '{viewId}' must be added before adding menu items.");
        }

        var customView = _customViews[viewIndex];
        var menuItems = customView.ViewMenuItems
            .Append(new CustomShellViewMenuItemContribution(
                viewId,
                id,
                title,
                typeof(TComponent),
                order,
                description))
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _customViews[viewIndex] = customView with { MenuItems = menuItems };
        return this;
    }

    public ICloudShellExtensionBuilder UseStartRoute(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        _startRoute = NormalizeRoute(route);
        return this;
    }

    public ICloudShellExtensionBuilder UseStartView(string viewId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewId);

        _startRoute = GetRegisteredView(viewId).Route;
        return this;
    }

    public ICloudShellExtensionBuilder UseStartView<TView>() =>
        UseStartView(GetRegisteredView(typeof(TView)).Id);

    public ICloudShellExtensionBuilder AddResourceProvider<TProvider>()
        where TProvider : class, IResourceProvider
    {
        _resourceProviderTypes.Add(typeof(TProvider));
        Services.AddSingleton<TProvider>();
        Services.AddSingleton<IResourceProvider>(
            serviceProvider => serviceProvider.GetRequiredService<TProvider>());
        return this;
    }

    public ICloudShellExtensionBuilder AddLogProvider<TProvider>()
        where TProvider : class, ILogProvider
    {
        _logProviderTypes.Add(typeof(TProvider));
        Services.AddSingleton<TProvider>();
        Services.AddSingleton<ILogProvider>(
            serviceProvider => serviceProvider.GetRequiredService<TProvider>());
        return this;
    }

    public ICloudShellExtensionBuilder AddResourceType<TRegistrationComponent>(
        string id,
        string displayName,
        string description,
        string icon,
        int order,
        ResourceTypeProbeOptions? probeOptions = null,
        ResourceClass resourceClass = ResourceClass.Generic)
    {
        AddResourceType(
            id,
            displayName,
            description,
            icon,
            order,
            typeof(TRegistrationComponent),
            null,
            probeOptions,
            resourceClass);

        return this;
    }

    public ICloudShellExtensionBuilder AddResourceType<TRegistrationComponent, TUpdateComponent>(
        string id,
        string displayName,
        string description,
        string icon,
        int order,
        ResourceTypeProbeOptions? probeOptions = null,
        ResourceClass resourceClass = ResourceClass.Generic)
    {
        AddResourceType(
            id,
            displayName,
            description,
            icon,
            order,
            typeof(TRegistrationComponent),
            typeof(TUpdateComponent),
            probeOptions,
            resourceClass);

        return this;
    }

    public ICloudShellExtensionBuilder AddResourceTypeEndpoint(
        string resourceTypeId,
        ResourceEndpointDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceTypeId);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Protocol);
        if (descriptor.TargetPort <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                "Resource endpoint descriptor target port must be greater than zero.");
        }

        var typeIndex = _resourceTypes.FindIndex(type =>
            string.Equals(type.Id, resourceTypeId, StringComparison.OrdinalIgnoreCase));
        if (typeIndex < 0)
        {
            throw new InvalidOperationException(
                $"Resource type '{resourceTypeId}' must be added before adding endpoint descriptors.");
        }

        var normalized = descriptor with
        {
            Name = descriptor.Name.Trim(),
            Protocol = descriptor.Protocol.Trim().ToLowerInvariant()
        };
        var resourceType = _resourceTypes[typeIndex];
        var descriptors = resourceType.ResourceEndpointDescriptors
            .Where(endpoint => !string.Equals(endpoint.Name, normalized.Name, StringComparison.OrdinalIgnoreCase))
            .Append(normalized)
            .ToArray();

        _resourceTypes[typeIndex] = resourceType with { EndpointDescriptors = descriptors };
        return this;
    }

    public ICloudShellExtensionBuilder AddResourceTab<TComponent>(
        string resourceTypeId,
        ResourceViewId id,
        string title,
        int order,
        bool showsApplyButton = false,
        string? groupTitle = null,
        string? icon = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ValidateTabId(id);

        var typeIndex = _resourceTypes.FindIndex(type =>
            string.Equals(type.Id, resourceTypeId, StringComparison.OrdinalIgnoreCase));
        if (typeIndex < 0)
        {
            throw new InvalidOperationException(
                $"Resource type '{resourceTypeId}' must be added before adding resource tabs.");
        }

        ValidatePredefinedViewReplacement(id);

        var resourceType = _resourceTypes[typeIndex];
        var tabs = resourceType.ResourceTabs
            .Append(new ResourceTabContribution(
                id,
                title,
                order,
                typeof(TComponent),
                showsApplyButton,
                NormalizeGroupTitle(groupTitle),
                ResolveResourceTabIcon(id, icon)))
            .OrderBy(tab => tab.Order)
            .ThenBy(tab => tab.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _resourceTypes[typeIndex] = resourceType with { Tabs = tabs };
        return this;
    }

    public ICloudShellExtensionBuilder AddResourcePredefinedViewSection<TComponent>(
        string resourceTypeId,
        ResourceViewId viewId,
        string id,
        string title,
        int order)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var typeIndex = _resourceTypes.FindIndex(type =>
            string.Equals(type.Id, resourceTypeId, StringComparison.OrdinalIgnoreCase));
        if (typeIndex < 0)
        {
            throw new InvalidOperationException(
                $"Resource type '{resourceTypeId}' must be added before adding predefined view sections.");
        }

        ValidatePredefinedViewSectionHost(viewId);

        var resourceType = _resourceTypes[typeIndex];
        var sections = resourceType.ResourcePredefinedViewSections
            .Append(new ResourcePredefinedViewSectionContribution(
                viewId,
                id.Trim(),
                title.Trim(),
                order,
                typeof(TComponent)))
            .OrderBy(section => section.Order)
            .ThenBy(section => section.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _resourceTypes[typeIndex] = resourceType with { PredefinedViewSections = sections };
        return this;
    }

    private static void ValidatePredefinedViewReplacement(ResourceViewId id)
    {
        if (!ResourcePredefinedViews.TryGet(id, out var definition))
        {
            return;
        }

        if (!definition.SupportsReplacement)
        {
            throw new InvalidOperationException(
                $"Predefined resource view '{definition.Id}' cannot be replaced by a provider-owned tab.");
        }
    }

    private static void ValidatePredefinedViewSectionHost(ResourceViewId viewId)
    {
        if (!ResourcePredefinedViews.TryGet(viewId, out var definition))
        {
            throw new InvalidOperationException(
                $"Predefined resource view sections can only target known predefined views. '{viewId}' is not registered.");
        }

        if (!definition.SupportsSections)
        {
            throw new InvalidOperationException(
                $"Predefined resource view '{definition.Id}' does not accept provider-owned sections.");
        }
    }

    private static string? ResolveResourceTabIcon(ResourceViewId id, string? icon)
    {
        if (!string.IsNullOrWhiteSpace(icon))
        {
            return icon.Trim();
        }

        return ResourcePredefinedViews.TryGet(id, out var definition)
            ? definition.Icon
            : null;
    }

    private static void ValidateTabId(ResourceViewId id)
    {
        if (string.IsNullOrWhiteSpace(id.GroupId) || string.IsNullOrWhiteSpace(id.Identifier))
        {
            throw new InvalidOperationException(
                $"Resource tab ID '{id}' is invalid. Expected '<group-id>:<identifier>'.");
        }
    }

    private static string? NormalizeGroupTitle(string? groupTitle) =>
        string.IsNullOrWhiteSpace(groupTitle) ? null : groupTitle.Trim();

    private void AddResourceType(
        string id,
        string displayName,
        string description,
        string icon,
        int order,
        Type registrationComponentType,
        Type? updateComponentType,
        ResourceTypeProbeOptions? probeOptions,
        ResourceClass resourceClass)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        _resourceTypes.Add(new ResourceTypeContribution(
            id,
            displayName,
            description,
            icon,
            order,
            registrationComponentType,
            updateComponentType,
            ProbeOptions: probeOptions,
            ResourceClass: resourceClass));
    }

    private void AddNavigationItem(
        string id,
        string text,
        NavItemTarget target,
        string icon,
        int order,
        string group,
        string? parentId,
        IReadOnlyList<string>? requiredPermissions,
        bool replacesExisting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(target);

        var href = ResolveTargetHref(target);

        _navigationItems.Add(new NavItemContribution(
            id,
            text,
            href,
            target,
            icon,
            order,
            group,
            replacesExisting,
            ParentId: parentId,
            RequiredPermissions: requiredPermissions));
    }

    private ShellViewContribution GetRegisteredView(string viewId) =>
        _views.FirstOrDefault(view =>
            string.Equals(view.Id, viewId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(
            $"View '{viewId}' must be registered before adding navigation items or start routes that reference it.");

    private ShellViewContribution GetRegisteredView(Type viewType) =>
        _views.FirstOrDefault(view => view.ComponentType == viewType)
        ?? throw new InvalidOperationException(
            $"View component '{viewType.FullName}' must be registered before adding navigation items or start routes that reference it.");

    private string ResolveTargetHref(NavItemTarget target)
    {
        if (!string.IsNullOrWhiteSpace(target.ViewId))
        {
            return GetRegisteredView(target.ViewId).Route;
        }

        if (target.ViewType is not null)
        {
            return GetRegisteredView(target.ViewType).Route;
        }

        if (!string.IsNullOrWhiteSpace(target.Href))
        {
            return NormalizeHref(target.Href);
        }

        throw new InvalidOperationException("Navigation item targets must specify a view id or href.");
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
        new(
            manifest,
            activationPolicy,
            _navigationItems.ToArray(),
            _views.ToArray(),
            _resourceTypes.ToArray(),
            _resourceProviderTypes.ToArray(),
            _logProviderTypes.ToArray(),
            _customViews.ToArray(),
            _startRoute);

    private static string NormalizeRoute(string route) =>
        route == "/" ? route : "/" + route.Trim('/');

    private static string NormalizeHref(string href) =>
        Uri.TryCreate(href, UriKind.Absolute, out _)
            ? href
            : NormalizeRoute(href);

    private static string GetPrimaryRoute(Type componentType)
    {
        var routes = componentType
            .GetCustomAttributes(inherit: false)
            .Where(attribute => attribute.GetType().FullName == "Microsoft.AspNetCore.Components.RouteAttribute")
            .Select(attribute => attribute.GetType().GetProperty("Template")?.GetValue(attribute) as string)
            .Where(route => !string.IsNullOrWhiteSpace(route))
            .Select(route => NormalizeRoute(route!))
            .ToArray();

        if (routes.Length == 0)
        {
            throw new InvalidOperationException(
                $"View component '{componentType.FullName}' must declare at least one @page route.");
        }

        return routes[0];
    }

    private static IReadOnlyList<ShellViewRouteParameter> GetRouteParameters(string route)
    {
        var parameters = new List<ShellViewRouteParameter>();
        var searchIndex = 0;

        while (searchIndex < route.Length)
        {
            var startIndex = route.IndexOf('{', searchIndex);
            if (startIndex < 0)
            {
                break;
            }

            var endIndex = route.IndexOf('}', startIndex + 1);
            if (endIndex < 0)
            {
                break;
            }

            var token = route[startIndex..(endIndex + 1)];
            var content = route[(startIndex + 1)..endIndex];
            var isCatchAll = content.StartsWith('*');
            if (isCatchAll)
            {
                content = content[1..];
            }

            var isOptional = content.EndsWith('?');
            if (isOptional)
            {
                content = content[..^1];
            }

            var constraintIndex = content.IndexOf(':');
            if (constraintIndex >= 0)
            {
                content = content[..constraintIndex];
            }

            if (!string.IsNullOrWhiteSpace(content))
            {
                parameters.Add(new ShellViewRouteParameter(
                    content,
                    token,
                    isOptional,
                    isCatchAll));
            }

            searchIndex = endIndex + 1;
        }

        return parameters;
    }
}
