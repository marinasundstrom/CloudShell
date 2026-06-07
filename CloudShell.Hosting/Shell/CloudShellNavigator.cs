using CloudShell.Abstractions.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using System.Globalization;
using System.Reflection;

namespace CloudShell.Hosting.Shell;

public sealed class CloudShellNavigator(
    ShellCatalog shellCatalog,
    NavigationManager navigationManager) : ICloudShellNavigator
{
    public string GetHref<TView>(object? routeValues = null) =>
        BuildViewHref(
            shellCatalog.GetView(typeof(TView))
                ?? throw new InvalidOperationException($"View component '{typeof(TView).FullName}' is not registered or is not active."),
            routeValues);

    public string GetHref(string viewId, object? routeValues = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewId);

        var view = shellCatalog.GetView(viewId)
            ?? throw new InvalidOperationException($"View '{viewId}' is not registered or is not active.");

        return BuildViewHref(view, routeValues);
    }

    public string GetHref(NavItemTarget target, object? routeValues = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!string.IsNullOrWhiteSpace(target.ViewId))
        {
            return GetHref(target.ViewId, routeValues);
        }

        if (target.ViewType is not null)
        {
            return BuildViewHref(
                shellCatalog.GetView(target.ViewType)
                    ?? throw new InvalidOperationException($"View component '{target.ViewType.FullName}' is not registered or is not active."),
                routeValues);
        }

        if (!string.IsNullOrWhiteSpace(target.Href))
        {
            return NormalizeHref(target.Href);
        }

        throw new InvalidOperationException("Navigation targets must specify a view id or href.");
    }

    public void NavigateTo<TView>(object? routeValues = null, bool forceLoad = false, bool replace = false) =>
        NavigateTo(NavItemTarget.ForView<TView>(), routeValues, forceLoad, replace);

    public void NavigateToView(string viewId, object? routeValues = null, bool forceLoad = false, bool replace = false) =>
        NavigateToHref(GetHref(viewId, routeValues), forceLoad, replace);

    public void NavigateTo(NavItemTarget target, object? routeValues = null, bool forceLoad = false, bool replace = false) =>
        NavigateToHref(GetHref(target, routeValues), forceLoad, replace);

    public void NavigateToHref(string href, bool forceLoad = false, bool replace = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(href);

        navigationManager.NavigateTo(NormalizeHref(href), forceLoad, replace);
    }

    private static string BuildViewHref(ShellViewContribution view, object? routeValues)
    {
        var values = ToRouteValues(routeValues);
        var usedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var href = view.Route;

        foreach (var parameter in view.RouteParameters)
        {
            if (!TryGetRouteValue(values, parameter.Name, out var value) ||
                value is null ||
                string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture)))
            {
                if (parameter.IsOptional)
                {
                    href = RemoveOptionalSegment(href, parameter.Token);
                    continue;
                }

                throw new InvalidOperationException(
                    $"View '{view.Id}' requires route value '{parameter.Name}'.");
            }

            usedValues.Add(parameter.Name);
            href = href.Replace(
                parameter.Token,
                EncodeRouteValue(value, parameter.IsCatchAll),
                StringComparison.OrdinalIgnoreCase);
        }

        var query = values
            .Where(value => !usedValues.Contains(value.Key) && value.Value is not null)
            .ToDictionary(
                value => value.Key,
                value => Convert.ToString(value.Value, CultureInfo.InvariantCulture),
                StringComparer.OrdinalIgnoreCase);

        return query.Count == 0
            ? href
            : QueryHelpers.AddQueryString(href, query);
    }

    private static IReadOnlyDictionary<string, object?> ToRouteValues(object? routeValues)
    {
        if (routeValues is null)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        if (routeValues is IEnumerable<KeyValuePair<string, object?>> objectValues)
        {
            return objectValues.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase);
        }

        if (routeValues is IEnumerable<KeyValuePair<string, string?>> stringValues)
        {
            return stringValues.ToDictionary(
                item => item.Key,
                item => (object?)item.Value,
                StringComparer.OrdinalIgnoreCase);
        }

        return routeValues
            .GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0)
            .ToDictionary(
                property => property.Name,
                property => property.GetValue(routeValues),
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryGetRouteValue(
        IReadOnlyDictionary<string, object?> values,
        string name,
        out object? value)
    {
        if (values.TryGetValue(name, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    private static string RemoveOptionalSegment(string href, string token)
    {
        var segment = "/" + token;
        if (href.Contains(segment, StringComparison.OrdinalIgnoreCase))
        {
            return href.Replace(segment, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return href.Replace(token, string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string EncodeRouteValue(object value, bool isCatchAll)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (!isCatchAll)
        {
            return Uri.EscapeDataString(text);
        }

        return string.Join(
            "/",
            text.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
    }

    private static string NormalizeHref(string href)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out _) ||
            href.StartsWith('#') ||
            href.StartsWith('?') ||
            href.StartsWith('/'))
        {
            return href;
        }

        return "/" + href.Trim('/');
    }
}
