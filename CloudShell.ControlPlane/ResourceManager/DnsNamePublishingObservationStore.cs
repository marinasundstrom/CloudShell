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
        IReadOnlyDictionary<string, string>? attributes = null,
        DateTimeOffset? observedAt = null) =>
        Record(
            context,
            providerName,
            DnsNamePublishingObservationStatus.Published,
            result.Message,
            attributes,
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
            null,
            observedAt);

    private void Record(
        DnsNamePublishingContext context,
        string providerName,
        DnsNamePublishingObservationStatus status,
        string message,
        IReadOnlyDictionary<string, string>? attributes,
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
            observedAt ?? DateTimeOffset.UtcNow,
            attributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
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
    DateTimeOffset ObservedAt,
    IReadOnlyDictionary<string, string> Attributes);

public interface INamePublishingObservationAttributeProvider
{
    IReadOnlyDictionary<string, string> GetObservationAttributes(DnsNamePublishingContext context);
}

public enum DnsNamePublishingObservationStatus
{
    Published,
    Failed
}
