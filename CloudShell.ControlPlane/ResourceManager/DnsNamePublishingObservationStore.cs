using System.Collections.Concurrent;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class DnsNamePublishingObservationStore
{
    private readonly ConcurrentDictionary<string, DnsNamePublishingObservation> observations =
        new(StringComparer.OrdinalIgnoreCase);

    public DnsNamePublishingObservation? GetObservation(string zoneResourceId) =>
        observations.TryGetValue(zoneResourceId, out var observation)
            ? observation
            : null;

    public void RecordPublished(
        DnsNamePublishingContext context,
        string providerName,
        ResourceProcedureResult result,
        DateTimeOffset? observedAt = null) =>
        Record(
            context,
            providerName,
            DnsNamePublishingObservationStatus.Published,
            result.Message,
            observedAt);

    public void RecordFailed(
        DnsNamePublishingContext context,
        string providerName,
        string message,
        DateTimeOffset? observedAt = null) =>
        Record(
            context,
            providerName,
            DnsNamePublishingObservationStatus.Failed,
            message,
            observedAt);

    private void Record(
        DnsNamePublishingContext context,
        string providerName,
        DnsNamePublishingObservationStatus status,
        string message,
        DateTimeOffset? observedAt)
    {
        observations[context.Definition.Id] = new DnsNamePublishingObservation(
            context.Definition.Id,
            providerName,
            status,
            context.Mappings
                .Select(mapping => NormalizeHostName(mapping.Mapping.HostName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            message,
            observedAt ?? DateTimeOffset.UtcNow);
    }

    private static string NormalizeHostName(string hostName) =>
        hostName.Trim().TrimEnd('.').ToLowerInvariant();
}

public sealed record DnsNamePublishingObservation(
    string ZoneResourceId,
    string ProviderName,
    DnsNamePublishingObservationStatus Status,
    IReadOnlyList<string> HostNames,
    string Message,
    DateTimeOffset ObservedAt);

public enum DnsNamePublishingObservationStatus
{
    Published,
    Failed
}
