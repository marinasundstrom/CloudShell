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

        return diagnostics;
    }

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
}

public sealed record ResourceDiagnosticView(
    string Severity,
    string Title,
    string Message);
