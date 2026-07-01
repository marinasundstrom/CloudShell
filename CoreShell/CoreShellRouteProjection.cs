using System.Globalization;
using System.Text;

namespace CoreShell;

internal static class CoreShellRouteProjection
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyRouteValues =
        new Dictionary<string, object?>();

    public static string NormalizeRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return "/";
        }

        var normalized = route.StartsWith('/', StringComparison.Ordinal)
            ? route
            : "/" + route;
        var suffixStart = IndexOfRouteSuffix(normalized);
        return suffixStart >= 0 ? normalized[..suffixStart] : normalized;
    }

    public static string AppendRouteValues(
        string route,
        IReadOnlyDictionary<string, object?>? routeValues)
    {
        if ((routeValues is null || routeValues.Count == 0) &&
            !route.Contains('{', StringComparison.Ordinal))
        {
            return route;
        }

        routeValues ??= EmptyRouteValues;
        var usedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var materializedRoute = TryMaterializeRouteTemplate(route, routeValues, usedValues);
        if (materializedRoute is null)
        {
            return "#";
        }

        var query = string.Join(
            '&',
            routeValues
                .Where(value => value.Value is not null && !usedValues.Contains(value.Key))
                .Select(value =>
                    $"{Uri.EscapeDataString(value.Key)}={Uri.EscapeDataString(Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty)}"));

        return string.IsNullOrEmpty(query)
            ? materializedRoute
            : AppendQuery(materializedRoute, query);
    }

    public static string ResolveSectionHref(
        CoreShellPageContribution page,
        CoreShellSectionOutletContribution outlet,
        CoreShellSectionContribution section,
        IReadOnlyDictionary<string, object?>? routeValues)
    {
        if (outlet.AddressMode == CoreShellSectionAddressMode.Child)
        {
            return AppendRouteValues(
                page.Route,
                WithRouteValue(
                    routeValues,
                    outlet.SelectionKey,
                    GetSectionSelectionValue(section, outlet)));
        }

        return $"{AppendRouteValues(page.Route, routeValues)}#{Uri.EscapeDataString(section.Id.Value)}";
    }

    public static bool RouteTemplateMatches(string template, string route)
    {
        var templateSegments = GetRouteSegments(template);
        var routeSegments = GetRouteSegments(route);
        var routeIndex = 0;

        for (var templateIndex = 0; templateIndex < templateSegments.Length; templateIndex++)
        {
            var templateSegment = templateSegments[templateIndex];
            if (TryParseSegmentParameter(templateSegment, out var parameter))
            {
                if (routeIndex >= routeSegments.Length)
                {
                    if (parameter.IsOptional)
                    {
                        continue;
                    }

                    return false;
                }

                routeIndex++;
                continue;
            }

            if (routeIndex >= routeSegments.Length ||
                !string.Equals(templateSegment, routeSegments[routeIndex], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            routeIndex++;
        }

        return routeIndex == routeSegments.Length;
    }

    public static int GetRouteTemplateSpecificity(string template)
    {
        var segments = GetRouteSegments(template);
        var literalSegments = segments.Count(segment => !TryParseSegmentParameter(segment, out _));
        var requiredParameters = segments.Count(segment =>
            TryParseSegmentParameter(segment, out var parameter) &&
            !parameter.IsOptional);
        return (literalSegments * 100) + (requiredParameters * 10) + segments.Length;
    }

    public static bool IsDirectHref(string value) =>
        value.StartsWith('/', StringComparison.Ordinal) ||
        value.StartsWith('#', StringComparison.Ordinal) ||
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, object?> WithRouteValue(
        IReadOnlyDictionary<string, object?>? routeValues,
        string name,
        string value)
    {
        var merged = routeValues is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : routeValues
                .Where(value => !string.Equals(value.Key, name, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(value => value.Key, value => value.Value, StringComparer.OrdinalIgnoreCase);

        merged[name] = value;
        return merged;
    }

    public static string GetSectionSelectionValue(
        CoreShellSectionContribution section,
        CoreShellSectionOutletContribution outlet)
    {
        var outletScope = StripKind("section-outlet", outlet.Id.Value);
        var outletScopedPrefix = $"section.{outletScope}.";
        if (section.Id.Value.StartsWith(outletScopedPrefix, StringComparison.OrdinalIgnoreCase) &&
            section.Id.Value.Length > outletScopedPrefix.Length)
        {
            return section.Id.Value[outletScopedPrefix.Length..];
        }

        var pageScope = StripKind("page", section.PageId.Value);
        var pageScopedPrefix = $"section.{pageScope}.";
        if (section.Id.Value.StartsWith(pageScopedPrefix, StringComparison.OrdinalIgnoreCase) &&
            section.Id.Value.Length > pageScopedPrefix.Length)
        {
            return section.Id.Value[pageScopedPrefix.Length..];
        }

        var lastSeparator = section.Id.Value.LastIndexOf('.');
        return lastSeparator >= 0 && lastSeparator + 1 < section.Id.Value.Length
            ? section.Id.Value[(lastSeparator + 1)..]
            : section.Id.Value;
    }

    private static string StripKind(string kind, string value)
    {
        var prefix = kind + ".";
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value[prefix.Length..]
            : value;
    }

    private static string? TryMaterializeRouteTemplate(
        string route,
        IReadOnlyDictionary<string, object?> routeValues,
        ISet<string> usedValues)
    {
        var result = new StringBuilder(route.Length);
        var cursor = 0;

        while (cursor < route.Length)
        {
            var openBrace = route.IndexOf('{', cursor);
            if (openBrace < 0)
            {
                result.Append(route, cursor, route.Length - cursor);
                return result.ToString();
            }

            var closeBrace = route.IndexOf('}', openBrace + 1);
            if (closeBrace < 0)
            {
                result.Append(route, cursor, route.Length - cursor);
                return result.ToString();
            }

            var token = route[(openBrace + 1)..closeBrace];
            var parameter = ParseRouteTemplateParameter(token);
            if (string.IsNullOrWhiteSpace(parameter.Name))
            {
                result.Append(route, cursor, closeBrace - cursor + 1);
                cursor = closeBrace + 1;
                continue;
            }

            var routeValue = routeValues.FirstOrDefault(value =>
                string.Equals(value.Key, parameter.Name, StringComparison.OrdinalIgnoreCase));
            if (routeValue.Key is null || routeValue.Value is null)
            {
                if (parameter.IsOptional && TryAppendWithoutOptionalSegment(route, result, cursor, openBrace, closeBrace))
                {
                    cursor = closeBrace + 1;
                    continue;
                }

                return null;
            }

            result.Append(route, cursor, openBrace - cursor);
            usedValues.Add(routeValue.Key);
            result.Append(Uri.EscapeDataString(
                Convert.ToString(routeValue.Value, CultureInfo.InvariantCulture) ?? string.Empty));
            cursor = closeBrace + 1;
        }

        return result.ToString();
    }

    private static bool TryAppendWithoutOptionalSegment(
        string route,
        StringBuilder result,
        int cursor,
        int openBrace,
        int closeBrace)
    {
        var prefixLength = openBrace - cursor;
        if (prefixLength <= 0)
        {
            return false;
        }

        var prefixEnd = openBrace - 1;
        var next = closeBrace + 1 < route.Length
            ? route[closeBrace + 1]
            : '\0';
        var isSegmentParameter =
            route[prefixEnd] == '/' &&
            (next == '\0' || next == '?' || next == '#');

        if (!isSegmentParameter)
        {
            return false;
        }

        result.Append(route, cursor, prefixLength - 1);
        return true;
    }

    private static string[] GetRouteSegments(string route)
    {
        var normalized = NormalizeRoute(route);
        return normalized == "/"
            ? []
            : normalized.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool TryParseSegmentParameter(
        string segment,
        out RouteTemplateParameter parameter)
    {
        parameter = default;
        if (segment.Length < 2 ||
            segment[0] != '{' ||
            segment[^1] != '}')
        {
            return false;
        }

        parameter = ParseRouteTemplateParameter(segment[1..^1]);
        return !string.IsNullOrWhiteSpace(parameter.Name);
    }

    private static RouteTemplateParameter ParseRouteTemplateParameter(string token)
    {
        var trimmed = token.Trim().TrimStart('*');
        var end = trimmed.IndexOfAny([':', '?']);
        var name = end < 0
            ? trimmed
            : trimmed[..end];
        return new(name, trimmed.Contains('?', StringComparison.Ordinal));
    }

    private static int IndexOfRouteSuffix(string route)
    {
        var templateDepth = 0;
        for (var index = 0; index < route.Length; index++)
        {
            var value = route[index];
            if (value == '{')
            {
                templateDepth++;
                continue;
            }

            if (value == '}')
            {
                templateDepth = Math.Max(0, templateDepth - 1);
                continue;
            }

            if (templateDepth == 0 && (value == '?' || value == '#'))
            {
                return index;
            }
        }

        return -1;
    }

    private static string AppendQuery(string route, string query)
    {
        var fragmentStart = route.IndexOf('#');
        var routeWithoutFragment = fragmentStart < 0
            ? route
            : route[..fragmentStart];
        var fragment = fragmentStart < 0
            ? string.Empty
            : route[fragmentStart..];
        var separator = routeWithoutFragment.Contains('?')
            ? "&"
            : "?";

        return $"{routeWithoutFragment}{separator}{query}{fragment}";
    }

    private readonly record struct RouteTemplateParameter(string Name, bool IsOptional);
}
