using System.Globalization;
using System.Text;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Traefik;

public static class TraefikDynamicConfigurationWriter
{
    public static string Write(LoadBalancerProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var httpRoutes = context.Routes
            .Where(route => route.Route.Kind == LoadBalancerRouteKind.Http)
            .ToArray();
        var tcpRoutes = context.Routes
            .Where(route => route.Route.Kind == LoadBalancerRouteKind.Tcp)
            .ToArray();
        var builder = new StringBuilder();

        if (httpRoutes.Length > 0)
        {
            WriteHttp(builder, httpRoutes);
        }

        if (tcpRoutes.Length > 0)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            WriteTcp(builder, tcpRoutes);
        }

        return builder.ToString();
    }

    private static void WriteHttp(
        StringBuilder builder,
        IReadOnlyList<LoadBalancerRouteResolution> routes)
    {
        builder.AppendLine("http:");
        builder.AppendLine("  routers:");
        foreach (var resolution in routes)
        {
            var route = resolution.Route;
            var routeId = CreateTraefikId(route.Id);
            builder.AppendLine(CultureInfo.InvariantCulture, $"    {routeId}:");
            builder.AppendLine(CultureInfo.InvariantCulture, $"      entryPoints: [{Quote(route.EntrypointName)}]");
            builder.AppendLine(CultureInfo.InvariantCulture, $"      rule: {Quote(CreateHttpRule(route.Match))}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"      service: {Quote(routeId)}");
        }

        builder.AppendLine("  services:");
        foreach (var resolution in routes)
        {
            var routeId = CreateTraefikId(resolution.Route.Id);
            builder.AppendLine(CultureInfo.InvariantCulture, $"    {routeId}:");
            builder.AppendLine("      loadBalancer:");
            builder.AppendLine("        servers:");
            var backends = resolution.ResolvedBackends;
            if (backends.Count > 0)
            {
                foreach (var backend in backends)
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"          - url: {Quote(CreateHttpTarget(backend))}");
                }
            }
            else
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"          - url: {Quote(CreateHttpTarget(resolution))}");
            }
        }
    }

    private static void WriteTcp(
        StringBuilder builder,
        IReadOnlyList<LoadBalancerRouteResolution> routes)
    {
        builder.AppendLine("tcp:");
        builder.AppendLine("  routers:");
        foreach (var resolution in routes)
        {
            var route = resolution.Route;
            var routeId = CreateTraefikId(route.Id);
            builder.AppendLine(CultureInfo.InvariantCulture, $"    {routeId}:");
            builder.AppendLine(CultureInfo.InvariantCulture, $"      entryPoints: [{Quote(route.EntrypointName)}]");
            builder.AppendLine(CultureInfo.InvariantCulture, $"      rule: {Quote(CreateTcpRule(route.Match))}");
            builder.AppendLine(CultureInfo.InvariantCulture, $"      service: {Quote(routeId)}");
        }

        builder.AppendLine("  services:");
        foreach (var resolution in routes)
        {
            var routeId = CreateTraefikId(resolution.Route.Id);
            builder.AppendLine(CultureInfo.InvariantCulture, $"    {routeId}:");
            builder.AppendLine("      loadBalancer:");
            builder.AppendLine("        servers:");
            var backends = resolution.ResolvedBackends;
            if (backends.Count > 0)
            {
                foreach (var backend in backends)
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"          - address: {Quote(CreateTcpTarget(backend))}");
                }
            }
            else
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"          - address: {Quote(CreateTcpTarget(resolution))}");
            }
        }
    }

    private static string CreateHttpRule(LoadBalancerRouteMatch match)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(match.Host))
        {
            parts.Add($"Host(`{match.Host.Trim()}`)");
        }

        if (!string.IsNullOrWhiteSpace(match.PathPrefix))
        {
            parts.Add($"PathPrefix(`{match.PathPrefix.Trim()}`)");
        }

        return parts.Count == 0 ? "PathPrefix(`/`)" : string.Join(" && ", parts);
    }

    private static string CreateTcpRule(LoadBalancerRouteMatch match) =>
        string.IsNullOrWhiteSpace(match.Host)
            ? "HostSNI(`*`)"
            : $"HostSNI(`{match.Host.Trim()}`)";

    private static string CreateHttpTarget(LoadBalancerRouteResolution resolution)
    {
        var targetAddress = ResolveTargetEndpointAddress(resolution);
        if (Uri.TryCreate(targetAddress, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
        {
            return targetAddress;
        }

        var port = ResolveTargetPort(resolution);
        return $"http://{CreateTargetHost(resolution.TargetResource)}:{port.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string CreateHttpTarget(LoadBalancerBackendTarget backend)
    {
        var scheme = string.Equals(backend.Protocol, "https", StringComparison.OrdinalIgnoreCase)
            ? "https"
            : "http";
        return $"{scheme}://{backend.Host}:{backend.Port.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string CreateTcpTarget(LoadBalancerRouteResolution resolution)
    {
        var targetAddress = ResolveTargetEndpointAddress(resolution);
        if (Uri.TryCreate(targetAddress, UriKind.Absolute, out var uri) &&
            !uri.IsDefaultPort)
        {
            return $"{uri.Host}:{uri.Port.ToString(CultureInfo.InvariantCulture)}";
        }

        var port = ResolveTargetPort(resolution);
        return $"{CreateTargetHost(resolution.TargetResource)}:{port.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string CreateTcpTarget(LoadBalancerBackendTarget backend) =>
        $"{backend.Host}:{backend.Port.ToString(CultureInfo.InvariantCulture)}";

    private static int ResolveTargetPort(LoadBalancerRouteResolution resolution)
    {
        if (resolution.Route.Target.Port is { } port)
        {
            return port;
        }

        var targetAddress = ResolveTargetEndpointAddress(resolution);
        if (Uri.TryCreate(targetAddress, UriKind.Absolute, out var uri) &&
            !uri.IsDefaultPort)
        {
            return uri.Port;
        }

        throw new InvalidOperationException(
            $"Route '{resolution.Route.Id}' target '{resolution.TargetResource.Id}' must specify a port or endpoint with a port.");
    }

    private static string? ResolveTargetEndpointAddress(LoadBalancerRouteResolution resolution) =>
        resolution.TargetEndpointNetworkMapping?.Address ?? resolution.TargetEndpoint?.Address;

    private static string CreateTargetHost(Resource resource)
    {
        var builder = new StringBuilder(resource.Id.Length);
        foreach (var character in resource.Id.Trim())
        {
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
        }

        var host = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(host) ? "localhost" : host;
    }

    private static string CreateTraefikId(string id)
    {
        var builder = new StringBuilder(id.Length);
        foreach (var character in id.Trim())
        {
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "route" : result;
    }

    private static string Quote(string value) =>
        $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
