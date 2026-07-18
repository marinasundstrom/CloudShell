using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
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
    private const string SqlCredentialResolvedEvent = "event.provider.applications.sql-server.credential.resolved";
    private const string SqlCredentialRequestDeniedEvent = "event.provider.applications.sql-server.credential.request.denied";
    private const string SqlCredentialRequestFailedEvent = "event.provider.applications.sql-server.credential.request.failed";

    public static string GetDisplayName(
        string eventType,
        IStringLocalizer<SharedResource> localizer)
    {
        if (string.Equals(eventType, ResourceEventTypes.Actions.Lifecycle.Start, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Start action"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Actions.ForFailedAction(ResourceActionIds.Start), StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Start action failed"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Actions.Lifecycle.Stop, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Stop action"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Actions.ForFailedAction(ResourceActionIds.Stop), StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Stop action failed"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Actions.Lifecycle.Pause, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Pause action"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Actions.ForFailedAction(ResourceActionIds.Pause), StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Pause action failed"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Actions.Lifecycle.Restart, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Restart action"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Actions.ForFailedAction(ResourceActionIds.Restart), StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Restart action failed"].Value;
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

        if (string.Equals(eventType, ResourceEventTypes.Events.Deployment.Applying, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Deployment applying"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Deployment.ServiceReconciling, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Service reconciling"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Deployment.ServiceReconciled, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Service reconciled"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Deployment.ReplicaMaterializing, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Replica starting"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Deployment.ReplicaMaterialized, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Replica running"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Deployment.RoutingUpdating, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Routing updating"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Deployment.RoutingUpdated, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Routing updated"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Deployment.RollingBack, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Rollback running"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Deployment.RolledBack, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Rollback completed"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Deployment.RollbackFailed, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Rollback failed"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Deployment.CleanupRunning, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Cleanup running"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Deployment.CleanupCompleted, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Cleanup completed"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Deployment.CleanupWarning, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Cleanup warning"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Deployment.Applied, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Deployment applied"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Deployment.Failed, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Deployment failed"].Value;
        }

        if (string.Equals(eventType, SqlCredentialResolvedEvent, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["SQL credential resolved"].Value;
        }

        if (string.Equals(eventType, SqlCredentialRequestDeniedEvent, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["SQL credential request denied"].Value;
        }

        if (string.Equals(eventType, SqlCredentialRequestFailedEvent, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["SQL credential request failed"].Value;
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
