using System.Globalization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

public static class ResourceDiagnosticDisplay
{
    public static IReadOnlyList<ResourceDiagnosticView> GetDiagnostics(
        Resource resource,
        IReadOnlyDictionary<string, Resource>? relatedResources = null)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var diagnostics = new List<ResourceDiagnosticView>();

        if (resource.ResourceAttributes.TryGetValue(ResourceAttributeNames.DnsConflictCount, out var conflictCountValue) &&
            int.TryParse(conflictCountValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var conflictCount) &&
            conflictCount > 0)
        {
            diagnostics.Add(new ResourceDiagnosticView(
                "Warning",
                "DNS name conflict",
                $"{conflictCount} name mappings in this DNS zone claim the same host name and exposure scope."));
        }

        if (resource.ResourceAttributes.TryGetValue(ResourceAttributeNames.NameMappingStatus, out var mappingStatus) &&
            !string.Equals(mappingStatus, "Ready", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new ResourceDiagnosticView(
                "Warning",
                string.Equals(mappingStatus, "Conflict", StringComparison.OrdinalIgnoreCase)
                    ? "Name mapping conflict"
                    : $"Name mapping status: {mappingStatus}",
                resource.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.NameMappingStatusReason) ??
                    "This name mapping is not ready."));
        }

        if (resource.ResourceAttributes.TryGetValue(
                ResourceAttributeNames.NameMappingMaterializationStatus,
                out var materializationStatus) &&
            string.Equals(materializationStatus, "LogicalOnly", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new ResourceDiagnosticView(
                "Warning",
                "Name mapping is logical only",
                resource.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.NameMappingMaterializationStatusReason) ??
                    "No DNS publishing provider is selected for this name mapping."));
        }

        AddNamePublisherDiagnostics(resource, relatedResources, diagnostics);
        AddEndpointMappingDiagnostics(resource, relatedResources, diagnostics);
        AddLoadBalancerDiagnostics(resource, relatedResources, diagnostics);
        AddVolumeMountMaterializationDiagnostics(resource, diagnostics);

        return diagnostics;
    }

    private static void AddVolumeMountMaterializationDiagnostics(
        Resource resource,
        List<ResourceDiagnosticView> diagnostics)
    {
        if (!resource.ResourceAttributes.TryGetValue(
                ResourceAttributeNames.VolumeMountMaterializationStatus,
                out var status) ||
            string.IsNullOrWhiteSpace(status) ||
            string.Equals(status, "materialized", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "notApplicable", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var materializedCount = GetAttributeInteger(
            resource,
            ResourceAttributeNames.VolumeMountMaterializedCount);
        var mountCount = GetAttributeInteger(
            resource,
            ResourceAttributeNames.VolumeMountCount);
        var message = FormatVolumeMountMaterializationMessage(status, materializedCount, mountCount);

        diagnostics.Add(new ResourceDiagnosticView(
            "Warning",
            "Storage mounts not fully materialized",
            message));
    }

    private static string FormatVolumeMountMaterializationMessage(
        string status,
        int? materializedCount,
        int? mountCount)
    {
        var countText = materializedCount is not null && mountCount is not null
            ? $" {materializedCount.Value} of {mountCount.Value} declared storage mounts are materialized."
            : string.Empty;

        return NormalizeVolumeMountMaterializationStatus(status) switch
        {
            "partial" => $"Only some declared storage mounts are materialized.{countText}",
            "notActive" => $"Declared storage mounts are not active.{countText}",
            "unknown" => $"CloudShell has not observed storage mount materialization yet.{countText}",
            _ => $"Storage mount materialization status is '{status}'.{countText}"
        };
    }

    private static string NormalizeVolumeMountMaterializationStatus(string status) =>
        status.Trim() switch
        {
            var value when string.Equals(value, "Partial", StringComparison.OrdinalIgnoreCase) => "partial",
            var value when string.Equals(value, "NotActive", StringComparison.OrdinalIgnoreCase) => "notActive",
            var value when string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase) => "unknown",
            var value => value
        };

    private static void AddNamePublisherDiagnostics(
        Resource resource,
        IReadOnlyDictionary<string, Resource>? relatedResources,
        List<ResourceDiagnosticView> diagnostics)
    {
        if (!resource.ResourceAttributes.TryGetValue(
                ResourceAttributeNames.NameMappingProviderResourceId,
                out var providerResourceId) ||
            string.IsNullOrWhiteSpace(providerResourceId))
        {
            return;
        }

        if (relatedResources is null ||
            !relatedResources.TryGetValue(providerResourceId, out var provider))
        {
            diagnostics.Add(new ResourceDiagnosticView(
                "Warning",
                "DNS publisher unavailable",
                $"Provider resource '{providerResourceId}' could not be found. CloudShell cannot verify that this name mapping can be published."));
            return;
        }

        if (!provider.HasCapability(ResourceCapabilityIds.NetworkingNamePublisher))
        {
            diagnostics.Add(new ResourceDiagnosticView(
                "Warning",
                "DNS publisher capability missing",
                $"Provider resource '{provider.Name}' does not advertise the DNS name publisher capability."));
        }
    }

    private static void AddEndpointMappingDiagnostics(
        Resource resource,
        IReadOnlyDictionary<string, Resource>? relatedResources,
        List<ResourceDiagnosticView> diagnostics)
    {
        foreach (var mapping in resource.ResourceEndpointMappings)
        {
            AddEndpointMappingProviderDiagnostic(resource, relatedResources, diagnostics, mapping);
            AddEndpointMappingEndpointDiagnostic(resource, relatedResources, diagnostics, mapping, mapping.Source, "source");
            AddEndpointMappingEndpointDiagnostic(resource, relatedResources, diagnostics, mapping, mapping.Target, "target");
        }
    }

    private static void AddEndpointMappingProviderDiagnostic(
        Resource resource,
        IReadOnlyDictionary<string, Resource>? relatedResources,
        List<ResourceDiagnosticView> diagnostics,
        ResourceEndpointMappingDefinition mapping)
    {
        var providerResourceId = FirstNonEmpty(
            mapping.ProviderResourceId,
            mapping.NetworkResourceId,
            mapping.Source.ResourceId);
        if (string.IsNullOrWhiteSpace(providerResourceId))
        {
            diagnostics.Add(new ResourceDiagnosticView(
                "Warning",
                "Endpoint mapping provider unavailable",
                $"Mapping '{mapping.Name}' does not specify a provider resource."));
            return;
        }

        var provider = FindResource(resource, relatedResources, providerResourceId);
        if (provider is null)
        {
            diagnostics.Add(new ResourceDiagnosticView(
                "Warning",
                "Endpoint mapping provider unavailable",
                $"Mapping '{mapping.Name}' requires provider resource '{providerResourceId}', but that resource could not be found."));
            return;
        }

        if (!provider.HasCapability(ResourceCapabilityIds.NetworkingEndpointMapper))
        {
            diagnostics.Add(new ResourceDiagnosticView(
                "Warning",
                "Endpoint mapping provider capability missing",
                $"Mapping '{mapping.Name}' provider resource '{provider.Name}' does not advertise the endpoint mapper capability."));
        }
    }

    private static void AddEndpointMappingEndpointDiagnostic(
        Resource resource,
        IReadOnlyDictionary<string, Resource>? relatedResources,
        List<ResourceDiagnosticView> diagnostics,
        ResourceEndpointMappingDefinition mapping,
        ResourceEndpointReference endpointReference,
        string role)
    {
        var endpointResource = FindResource(resource, relatedResources, endpointReference.ResourceId);
        if (endpointResource is null)
        {
            diagnostics.Add(new ResourceDiagnosticView(
                "Warning",
                $"Endpoint mapping {role} unavailable",
                $"Mapping '{mapping.Name}' {role} resource '{endpointReference.ResourceId}' could not be found."));
            return;
        }

        if (!endpointResource.Endpoints.Any(endpoint =>
            string.Equals(endpoint.Name, endpointReference.EndpointName, StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Add(new ResourceDiagnosticView(
                "Warning",
                $"Endpoint mapping {role} endpoint unavailable",
                $"Mapping '{mapping.Name}' {role} endpoint '{endpointReference.EndpointName}' could not be found on resource '{endpointResource.Name}'."));
        }
    }

    private static void AddLoadBalancerDiagnostics(
        Resource resource,
        IReadOnlyDictionary<string, Resource>? relatedResources,
        List<ResourceDiagnosticView> diagnostics)
    {
        if (!IsLoadBalancerResource(resource))
        {
            return;
        }

        AddLoadBalancerHostDiagnostic(resource, relatedResources, diagnostics);
        AddLoadBalancerRouteDiagnostics(resource, relatedResources, diagnostics);
    }

    private static void AddLoadBalancerHostDiagnostic(
        Resource resource,
        IReadOnlyDictionary<string, Resource>? relatedResources,
        List<ResourceDiagnosticView> diagnostics)
    {
        var hostResourceId = GetLoadBalancerHostResourceId(resource);
        if (string.IsNullOrWhiteSpace(hostResourceId))
        {
            return;
        }

        if (relatedResources is null ||
            !relatedResources.TryGetValue(hostResourceId, out _))
        {
            diagnostics.Add(new ResourceDiagnosticView(
                "Warning",
                "Load balancer host unavailable",
                $"Container host resource '{hostResourceId}' could not be found. Provider-owned load balancer runtime may not be placeable."));
        }
    }

    private static void AddLoadBalancerRouteDiagnostics(
        Resource resource,
        IReadOnlyDictionary<string, Resource>? relatedResources,
        List<ResourceDiagnosticView> diagnostics)
    {
        if (relatedResources is null)
        {
            return;
        }

        foreach (var route in resource.ResourceLoadBalancerRoutes)
        {
            if (!relatedResources.TryGetValue(route.Target.ResourceId, out var target))
            {
                diagnostics.Add(new ResourceDiagnosticView(
                    "Warning",
                    "Load balancer route target unavailable",
                    $"Route '{route.Name}' targets resource '{route.Target.ResourceId}', but that resource could not be found."));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(route.Target.EndpointName) &&
                !target.Endpoints.Any(endpoint =>
                    string.Equals(endpoint.Name, route.Target.EndpointName, StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(new ResourceDiagnosticView(
                    "Warning",
                    "Load balancer route endpoint unavailable",
                    $"Route '{route.Name}' targets endpoint '{route.Target.EndpointName}' on resource '{target.Name}', but that endpoint could not be found."));
            }
        }
    }

    private static bool IsLoadBalancerResource(Resource resource) =>
        resource.HasCapability(ResourceCapabilityIds.NetworkingLoadBalancer) ||
        resource.ResourceLoadBalancerRoutes.Count > 0 ||
        resource.ResourceAttributes.ContainsKey(ResourceAttributeNames.LoadBalancerProvider);

    private static string? GetLoadBalancerHostResourceId(Resource resource)
    {
        var hostResourceId = resource.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.LoadBalancerHostResourceId);
        return string.IsNullOrWhiteSpace(hostResourceId) ||
            string.Equals(hostResourceId, "default", StringComparison.OrdinalIgnoreCase)
                ? null
                : hostResourceId;
    }

    private static Resource? FindResource(
        Resource current,
        IReadOnlyDictionary<string, Resource>? relatedResources,
        string resourceId)
    {
        if (string.Equals(current.Id, resourceId, StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        return relatedResources?.GetValueOrDefault(resourceId);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static int? GetAttributeInteger(Resource resource, string attributeName) =>
        resource.ResourceAttributes.TryGetValue(attributeName, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
}

public sealed record ResourceDiagnosticView(
    string Severity,
    string Title,
    string Message);
