using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

public sealed record ResourceLifecycleTransition(string ResourceId, string ActionId);

public static class ResourceLifecycleTransitionResolver
{
    private static readonly IReadOnlyDictionary<string, string> ActiveLifecycleEvents =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceEventTypes.Actions.Lifecycle.Start] = ResourceActionIds.Start,
            [ResourceEventTypes.Events.Lifecycle.Starting] = ResourceActionIds.Start,
            [ResourceEventTypes.Actions.Lifecycle.Stop] = ResourceActionIds.Stop,
            [ResourceEventTypes.Events.Lifecycle.Stopping] = ResourceActionIds.Stop,
            [ResourceEventTypes.Actions.Lifecycle.Pause] = ResourceActionIds.Pause,
            [ResourceEventTypes.Events.Lifecycle.Pausing] = ResourceActionIds.Pause,
            [ResourceEventTypes.Actions.Lifecycle.Restart] = ResourceActionIds.Restart,
            [ResourceEventTypes.Events.Lifecycle.Restarting] = ResourceActionIds.Restart
        };

    private static readonly ISet<string> TerminalLifecycleEvents =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ResourceEventTypes.Actions.ForFailedAction(ResourceActionIds.Start),
            ResourceEventTypes.Events.Lifecycle.Started,
            ResourceEventTypes.Events.Lifecycle.StartFailed,
            ResourceEventTypes.Actions.ForFailedAction(ResourceActionIds.Stop),
            ResourceEventTypes.Events.Lifecycle.Stopped,
            ResourceEventTypes.Events.Lifecycle.StopFailed,
            ResourceEventTypes.Actions.ForFailedAction(ResourceActionIds.Pause),
            ResourceEventTypes.Events.Lifecycle.Paused,
            ResourceEventTypes.Events.Lifecycle.PauseFailed,
            ResourceEventTypes.Actions.ForFailedAction(ResourceActionIds.Restart),
            ResourceEventTypes.Events.Lifecycle.Restarted,
            ResourceEventTypes.Events.Lifecycle.RestartFailed
        };

    public static IReadOnlyList<ResourceLifecycleTransition> GetActiveTransitions(
        IEnumerable<ResourceEvent> events) =>
        events
            .Where(resourceEvent => !string.IsNullOrWhiteSpace(resourceEvent.ResourceId))
            .GroupBy(resourceEvent => resourceEvent.ResourceId, StringComparer.OrdinalIgnoreCase)
            .Select(GetActiveTransition)
            .Where(transition => transition is not null)
            .Select(transition => transition!)
            .ToArray();

    public static bool IsLifecycleAction(string actionId) =>
        string.Equals(actionId, ResourceActionIds.Start, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(actionId, ResourceActionIds.Stop, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(actionId, ResourceActionIds.Pause, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(actionId, ResourceActionIds.Restart, StringComparison.OrdinalIgnoreCase);

    private static ResourceLifecycleTransition? GetActiveTransition(IEnumerable<ResourceEvent> events)
    {
        foreach (var resourceEvent in events.OrderByDescending(resourceEvent => resourceEvent.Timestamp))
        {
            if (ActiveLifecycleEvents.TryGetValue(resourceEvent.EventType, out var actionId))
            {
                return new ResourceLifecycleTransition(resourceEvent.ResourceId, actionId);
            }

            if (TerminalLifecycleEvents.Contains(resourceEvent.EventType))
            {
                return null;
            }
        }

        return null;
    }
}
