using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Notifications;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Notifications;

public sealed class ResourceEventNotificationProjector(
    ICloudShellNotificationStore notifications,
    IEnumerable<IResourceEventNotificationRule> rules) : IResourceEventObserver
{
    private readonly IReadOnlyList<IResourceEventNotificationRule> rules = rules.ToArray();

    public void OnResourceEvent(ResourceEvent resourceEvent)
    {
        foreach (var rule in rules)
        {
            var notification = rule.CreateNotification(resourceEvent);
            if (notification is not null)
            {
                notifications.CreateNotification(notification);
            }
        }
    }
}

public sealed class DefaultResourceEventNotificationRule : IResourceEventNotificationRule
{
    public CreateCloudShellNotificationCommand? CreateNotification(ResourceEvent resourceEvent)
    {
        var recipientKey = NormalizeOptional(resourceEvent.TriggeredBy);
        if (recipientKey is null)
        {
            return null;
        }

        return new CreateCloudShellNotificationCommand(
            recipientKey,
            CreateTitle(resourceEvent),
            resourceEvent.Message,
            resourceEvent.Severity,
            CreateStatus(resourceEvent),
            Source: "Control Plane",
            ResourceId: resourceEvent.ResourceId,
            EventType: resourceEvent.EventType,
            EventId: CreateEventId(resourceEvent),
            CorrelationId: resourceEvent.TraceId,
            TemplateKey: "cloudshell.resource-event",
            Attributes: CreateAttributes(resourceEvent));
    }

    private static CloudShellNotificationStatus CreateStatus(ResourceEvent resourceEvent) =>
        resourceEvent.Severity switch
        {
            ResourceSignalSeverity.Success => CloudShellNotificationStatus.Succeeded,
            ResourceSignalSeverity.Warning => CloudShellNotificationStatus.NeedsAttention,
            ResourceSignalSeverity.Error => CloudShellNotificationStatus.Failed,
            _ when IsProgressEvent(resourceEvent.EventType) => CloudShellNotificationStatus.InProgress,
            _ => CloudShellNotificationStatus.Active
        };

    private static bool IsProgressEvent(string eventType)
    {
        var normalized = eventType.Trim().ToLowerInvariant();
        return normalized.EndsWith(".starting", StringComparison.Ordinal)
            || normalized.EndsWith(".stopping", StringComparison.Ordinal)
            || normalized.EndsWith(".pausing", StringComparison.Ordinal)
            || normalized.EndsWith(".restarting", StringComparison.Ordinal)
            || normalized.EndsWith(".applying", StringComparison.Ordinal)
            || normalized.EndsWith(".running", StringComparison.Ordinal)
            || normalized.EndsWith(".updating", StringComparison.Ordinal)
            || normalized.EndsWith(".materializing", StringComparison.Ordinal)
            || normalized.EndsWith(".reconciling", StringComparison.Ordinal);
    }

    private static string CreateTitle(ResourceEvent resourceEvent)
    {
        var name = resourceEvent.EventType
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? "event";

        return $"Resource {name}";
    }

    private static string CreateEventId(ResourceEvent resourceEvent)
    {
        var basis = string.Join(
            "|",
            resourceEvent.ResourceId,
            resourceEvent.EventType,
            resourceEvent.Timestamp.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            resourceEvent.TriggeredBy ?? string.Empty,
            resourceEvent.TraceId ?? string.Empty,
            resourceEvent.SpanId ?? string.Empty);

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(basis)))
            .ToLowerInvariant();
    }

    private static IReadOnlyDictionary<string, string> CreateAttributes(ResourceEvent resourceEvent)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["resourceId"] = resourceEvent.ResourceId,
            ["eventType"] = resourceEvent.EventType
        };

        if (!string.IsNullOrWhiteSpace(resourceEvent.TraceId))
        {
            attributes["traceId"] = resourceEvent.TraceId;
        }

        if (!string.IsNullOrWhiteSpace(resourceEvent.SpanId))
        {
            attributes["spanId"] = resourceEvent.SpanId;
        }

        return attributes;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
