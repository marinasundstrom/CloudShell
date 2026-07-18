using System.Globalization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

public static class StorageResourceDiagnostics
{
    public static IReadOnlyList<ResourceDiagnosticView> GetDiagnostics(
        Resource storage,
        IReadOnlyList<Resource> ownedVolumes,
        IReadOnlyList<Resource> resources)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(ownedVolumes);
        ArgumentNullException.ThrowIfNull(resources);

        var ownedVolumeIds = ownedVolumes
            .Where(volume => IsOwnedVolume(storage, volume))
            .Select(volume => volume.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ownedVolumeIds.Count == 0)
        {
            return [];
        }

        var consumerSummaries = resources
            .Where(resource => IsVolumeConsumer(resource, ownedVolumeIds))
            .Select(GetConsumerMaterializationSummary)
            .Where(summary => !string.IsNullOrWhiteSpace(summary))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (consumerSummaries.Length == 0)
        {
            return [];
        }

        var consumerText = consumerSummaries.Length == 1
            ? "1 consumer"
            : $"{consumerSummaries.Length.ToString(CultureInfo.InvariantCulture)} consumers";
        var verb = consumerSummaries.Length == 1 ? "reports" : "report";

        return
        [
            new ResourceDiagnosticView(
                "Warning",
                "Storage volume mounts not fully active",
                $"{consumerText} of volumes owned by this Storage resource {verb} storage mounts that are not fully active: {string.Join("; ", consumerSummaries)}.")
        ];
    }

    private static bool IsOwnedVolume(Resource storage, Resource volume) =>
        string.Equals(volume.EffectiveTypeId, "cloudshell.volume", StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(volume.ParentResourceId, storage.Id, StringComparison.OrdinalIgnoreCase) ||
            volume.ResourceAttributes.TryGetValue(ResourceAttributeNames.VolumeStorageResourceId, out var storageResourceId) &&
            string.Equals(storageResourceId, storage.Id, StringComparison.OrdinalIgnoreCase));

    private static bool IsVolumeConsumer(Resource resource, ISet<string> ownedVolumeIds) =>
        resource.DependsOn.Any(dependency => ownedVolumeIds.Contains(dependency));

    private static string? GetConsumerMaterializationSummary(Resource consumer)
    {
        if (!consumer.ResourceAttributes.TryGetValue(
                ResourceAttributeNames.VolumeMountMaterializationStatus,
                out var status) ||
            string.IsNullOrWhiteSpace(status) ||
            string.Equals(status, "materialized", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "notApplicable", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var label = GetVolumeMountMaterializationStatusLabel(status);
        var countText = TryGetAttributeInteger(
                consumer,
                ResourceAttributeNames.VolumeMountMaterializedCount,
                out var materializedCount) &&
            TryGetAttributeInteger(
                consumer,
                ResourceAttributeNames.VolumeMountCount,
                out var mountCount)
                ? $" ({materializedCount.ToString(CultureInfo.InvariantCulture)}/{mountCount.ToString(CultureInfo.InvariantCulture)} active)"
                : string.Empty;

        return $"{consumer.Name}: {label}{countText}";
    }

    private static string GetVolumeMountMaterializationStatusLabel(string status) =>
        status.Trim() switch
        {
            var value when string.Equals(value, "partial", StringComparison.OrdinalIgnoreCase) =>
                "partially active",
            var value when string.Equals(value, "notActive", StringComparison.OrdinalIgnoreCase) =>
                "not active",
            var value when string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase) =>
                "unknown",
            var value => value
        };

    private static bool TryGetAttributeInteger(
        Resource resource,
        string attributeName,
        out int value)
    {
        value = 0;
        return resource.ResourceAttributes.TryGetValue(attributeName, out var attributeValue) &&
            int.TryParse(attributeValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
