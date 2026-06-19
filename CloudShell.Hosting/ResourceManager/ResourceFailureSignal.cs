using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.Localization;
using Microsoft.Extensions.Localization;

namespace CloudShell.Hosting.ResourceManager;

public sealed record ResourceFailureSignal(
    ResourceFailureKind Kind,
    ResourceCalloutSeverity Severity,
    string Message,
    string EventType,
    DateTimeOffset Timestamp);

public enum ResourceFailureKind
{
    Start,
    Stop,
    Pause,
    Restart,
    Operation
}

internal static class ResourceFailureSignalResolver
{
    private const int MaxMessageLength = 220;
    private const string FailedEventSuffix = ".failed";

    public static ResourceFailureSignal? GetActiveFailure(IEnumerable<ResourceEvent> events)
    {
        var orderedEvents = events
            .OrderByDescending(resourceEvent => resourceEvent.Timestamp)
            .ToArray();

        var latestFailure = orderedEvents.FirstOrDefault(IsFailureEvent);
        if (latestFailure is null)
        {
            return null;
        }

        var latestLifecycleSuccess = orderedEvents.FirstOrDefault(IsLifecycleSuccessEvent);
        if (latestLifecycleSuccess is not null && latestLifecycleSuccess.Timestamp > latestFailure.Timestamp)
        {
            return null;
        }

        return new ResourceFailureSignal(
            GetFailureKind(latestFailure.EventType),
            GetFailureSeverity(latestFailure),
            TrimMessage(latestFailure.Message),
            latestFailure.EventType,
            latestFailure.Timestamp);
    }

    private static ResourceFailureKind GetFailureKind(string eventType)
    {
        if (MatchesStartFailure(eventType))
        {
            return ResourceFailureKind.Start;
        }

        if (MatchesStopFailure(eventType))
        {
            return ResourceFailureKind.Stop;
        }

        if (MatchesPauseFailure(eventType))
        {
            return ResourceFailureKind.Pause;
        }

        if (MatchesRestartFailure(eventType))
        {
            return ResourceFailureKind.Restart;
        }

        return ResourceFailureKind.Operation;
    }

    private static ResourceCalloutSeverity GetFailureSeverity(ResourceEvent resourceEvent) =>
        string.Equals(resourceEvent.Level, "Warning", StringComparison.OrdinalIgnoreCase)
            ? ResourceCalloutSeverity.Warning
            : ResourceCalloutSeverity.Error;

    private static string TrimMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var trimmed = message.Trim();
        return trimmed.Length <= MaxMessageLength
            ? trimmed
            : string.Concat(trimmed.AsSpan(0, MaxMessageLength - 3), "...");
    }

    private static bool IsFailureEvent(ResourceEvent resourceEvent) =>
        resourceEvent.EventType.EndsWith(FailedEventSuffix, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resourceEvent.Level, "Error", StringComparison.OrdinalIgnoreCase);

    private static bool IsLifecycleSuccessEvent(ResourceEvent resourceEvent) =>
        MatchesAny(
            resourceEvent.EventType,
            ResourceEventTypes.Events.Lifecycle.Started,
            ResourceEventTypes.Events.Lifecycle.Stopped,
            ResourceEventTypes.Events.Lifecycle.Paused,
            ResourceEventTypes.Events.Lifecycle.Restarted);

    private static bool MatchesStartFailure(string eventType) =>
        MatchesAny(
            eventType,
            ResourceEventTypes.Actions.ForFailedAction(ResourceActionIds.Start),
            ResourceEventTypes.Events.Lifecycle.StartFailed);

    private static bool MatchesStopFailure(string eventType) =>
        MatchesAny(
            eventType,
            ResourceEventTypes.Actions.ForFailedAction(ResourceActionIds.Stop),
            ResourceEventTypes.Events.Lifecycle.StopFailed);

    private static bool MatchesPauseFailure(string eventType) =>
        MatchesAny(
            eventType,
            ResourceEventTypes.Actions.ForFailedAction(ResourceActionIds.Pause),
            ResourceEventTypes.Events.Lifecycle.PauseFailed);

    private static bool MatchesRestartFailure(string eventType) =>
        MatchesAny(
            eventType,
            ResourceEventTypes.Actions.ForFailedAction(ResourceActionIds.Restart),
            ResourceEventTypes.Events.Lifecycle.RestartFailed);

    private static bool MatchesAny(string eventType, params string[] candidates) =>
        candidates.Any(candidate => string.Equals(eventType, candidate, StringComparison.OrdinalIgnoreCase));
}

internal static class ResourceFailureSignalDisplay
{
    public static string GetTitle(
        ResourceFailureSignal signal,
        IStringLocalizer<SharedResource> localizer) =>
        signal.Kind switch
        {
            ResourceFailureKind.Start => localizer["Failed to start"].Value,
            ResourceFailureKind.Stop => localizer["Failed to stop"].Value,
            ResourceFailureKind.Pause => localizer["Failed to pause"].Value,
            ResourceFailureKind.Restart => localizer["Failed to restart"].Value,
            _ => localizer["Resource operation failed"].Value
        };

    public static string GetMessage(
        ResourceFailureSignal signal,
        IStringLocalizer<SharedResource> localizer) =>
        string.IsNullOrWhiteSpace(signal.Message)
            ? localizer["Review resource activity for failure details."].Value
            : signal.Message;

    public static string GetTooltip(
        ResourceFailureSignal signal,
        IStringLocalizer<SharedResource> localizer)
    {
        var title = GetTitle(signal, localizer);
        var message = GetMessage(signal, localizer);
        return string.IsNullOrWhiteSpace(message)
            ? title
            : $"{title}: {message}";
    }
}
