using CloudShell.Abstractions.Logs;
using CloudShell.Hosting.Localization;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace CloudShell.Hosting.ResourceManager;

internal static class ResourceEventDisplayNames
{
    private const string EventPrefix = "event.";
    private const string LifecycleActionPrefix = "action.lifecycle.";
    private const string LifecycleEventPrefix = "event.lifecycle.";
    private const string ConfigurationEventPrefix = "event.configuration.";
    private const string DeploymentEventPrefix = "event.deployment.";

    public static string GetDisplayName(
        string eventType,
        IStringLocalizer<SharedResource> localizer)
    {
        if (string.Equals(eventType, ResourceEventTypes.Actions.Lifecycle.Start, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Start action"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Actions.Lifecycle.Stop, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Stop action"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Actions.Lifecycle.Pause, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Pause action"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Actions.Lifecycle.Restart, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Restart action"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Lifecycle.Starting, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Starting"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Lifecycle.Started, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Started"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Lifecycle.StartFailed, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Start failed"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Lifecycle.Stopping, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Stopping"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Lifecycle.Stopped, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Stopped"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Lifecycle.StopFailed, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Stop failed"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Lifecycle.Pausing, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Pausing"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Lifecycle.Paused, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Paused"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Lifecycle.PauseFailed, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Pause failed"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Lifecycle.Restarting, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Restarting"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Lifecycle.Restarted, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Restarted"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Lifecycle.RestartFailed, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Restart failed"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Configuration.AppSettingsUpdated, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["App settings updated"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Configuration.EnvironmentVariablesUpdated, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Environment variables updated"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Deployment.ImageUpdated, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Image updated"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Deployment.ReplicasUpdated, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Replicas updated"].Value;
        }

        return FormatCustomEventDisplayName(eventType);
    }

    public static string GetGroupName(
        string eventType,
        IStringLocalizer<SharedResource> localizer) =>
        GetGroupKey(eventType) switch
        {
            ResourceEventGroupKey.LifecycleAction => localizer["Lifecycle actions"].Value,
            ResourceEventGroupKey.LifecycleEvent => localizer["Lifecycle events"].Value,
            ResourceEventGroupKey.ConfigurationEvent => localizer["Configuration events"].Value,
            ResourceEventGroupKey.DeploymentEvent => localizer["Deployment events"].Value,
            ResourceEventGroupKey.Action => localizer["Actions"].Value,
            ResourceEventGroupKey.Event => localizer["Events"].Value,
            _ => localizer["Activity"].Value
        };

    public static string GetGroupClass(string eventType) =>
        GetGroupKey(eventType) switch
        {
            ResourceEventGroupKey.LifecycleAction => "lifecycle-action",
            ResourceEventGroupKey.LifecycleEvent => "lifecycle-event",
            ResourceEventGroupKey.ConfigurationEvent => "configuration-event",
            ResourceEventGroupKey.DeploymentEvent => "deployment-event",
            ResourceEventGroupKey.Action => "action",
            ResourceEventGroupKey.Event => "event",
            _ => "activity"
        };

    public static int GetGroupOrder(string eventType) =>
        GetGroupKey(eventType) switch
        {
            ResourceEventGroupKey.LifecycleAction => 0,
            ResourceEventGroupKey.LifecycleEvent => 1,
            ResourceEventGroupKey.ConfigurationEvent => 2,
            ResourceEventGroupKey.DeploymentEvent => 3,
            ResourceEventGroupKey.Action => 4,
            ResourceEventGroupKey.Event => 5,
            _ => 6
        };

    private static ResourceEventGroupKey GetGroupKey(string eventType)
    {
        if (eventType.StartsWith(LifecycleActionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ResourceEventGroupKey.LifecycleAction;
        }

        if (eventType.StartsWith(LifecycleEventPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ResourceEventGroupKey.LifecycleEvent;
        }

        if (eventType.StartsWith(ConfigurationEventPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ResourceEventGroupKey.ConfigurationEvent;
        }

        if (eventType.StartsWith(DeploymentEventPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ResourceEventGroupKey.DeploymentEvent;
        }

        if (eventType.StartsWith(ResourceEventTypes.Actions.Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return ResourceEventGroupKey.Action;
        }

        if (eventType.StartsWith(EventPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ResourceEventGroupKey.Event;
        }

        return ResourceEventGroupKey.Activity;
    }

    private static string FormatCustomEventDisplayName(string eventType)
    {
        var value = eventType;
        if (value.StartsWith(ResourceEventTypes.Actions.Prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[ResourceEventTypes.Actions.Prefix.Length..];
        }
        else if (value.StartsWith(EventPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[EventPrefix.Length..];
        }

        var words = value
            .Replace('.', ' ')
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return eventType;
        }

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
            string.Join(' ', words));
    }

    private enum ResourceEventGroupKey
    {
        LifecycleAction,
        LifecycleEvent,
        ConfigurationEvent,
        DeploymentEvent,
        Action,
        Event,
        Activity
    }
}
