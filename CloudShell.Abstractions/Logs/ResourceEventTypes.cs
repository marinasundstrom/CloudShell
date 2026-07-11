using System.Text;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Logs;

public static class ResourceEventTypes
{
    public static class Actions
    {
        public const string Prefix = "action.";

        public static class Lifecycle
        {
            public const string Start = "action.lifecycle.start";
            public const string Stop = "action.lifecycle.stop";
            public const string Pause = "action.lifecycle.pause";
            public const string Restart = "action.lifecycle.restart";
        }

        public static string ForAction(string actionId) =>
            actionId.Trim().ToLowerInvariant() switch
            {
                ResourceActionIds.Start => Lifecycle.Start,
                ResourceActionIds.Stop => Lifecycle.Stop,
                ResourceActionIds.Pause => Lifecycle.Pause,
                ResourceActionIds.Restart => Lifecycle.Restart,
                _ => $"{Prefix}{NormalizeEventTypeSegment(actionId)}"
            };

        public static string ForFailedAction(string actionId) =>
            $"{ForAction(actionId)}.failed";
    }

    public static class Events
    {
        public static class Provider
        {
            public const string Prefix = "event.provider.";

            public static string ForEvent(string providerId, string eventName) =>
                $"{Prefix}{NormalizeEventTypeSegment(providerId)}.{NormalizeEventTypeSegment(eventName)}";
        }

        public static class Resource
        {
            public const string Creating = "event.resource.creating";
            public const string Created = "event.resource.created";
            public const string CreateFailed = "event.resource.create.failed";
        }

        public static class Lifecycle
        {
            public const string Starting = "event.lifecycle.starting";
            public const string Started = "event.lifecycle.started";
            public const string StartFailed = "event.lifecycle.start.failed";
            public const string Stopping = "event.lifecycle.stopping";
            public const string Stopped = "event.lifecycle.stopped";
            public const string StopFailed = "event.lifecycle.stop.failed";
            public const string Pausing = "event.lifecycle.pausing";
            public const string Paused = "event.lifecycle.paused";
            public const string PauseFailed = "event.lifecycle.pause.failed";
            public const string Restarting = "event.lifecycle.restarting";
            public const string Restarted = "event.lifecycle.restarted";
            public const string RestartFailed = "event.lifecycle.restart.failed";
            public const string Degraded = "event.lifecycle.degraded";
            public const string StoppedUnexpectedly = "event.lifecycle.stopped.unexpectedly";
        }

        public static class Recovery
        {
            public const string SignalFailed = "event.recovery.signal.failed";
            public const string RestartAttempted = "event.recovery.restart.attempted";
            public const string RestartScheduled = "event.recovery.restart.scheduled";
            public const string RestartSucceeded = "event.recovery.restart.succeeded";
            public const string RestartFailed = "event.recovery.restart.failed";
            public const string RestartSkipped = "event.recovery.restart.skipped";
            public const string RestartExhausted = "event.recovery.restart.exhausted";
            public const string Reset = "event.recovery.reset";
        }

        public static class ReplicaManagement
        {
            public const string Prefix = "event.replica.";
            public const string SlotVacant = "event.replica.slot.vacant";
            public const string SlotUnhealthy = "event.replica.slot.unhealthy";
            public const string OccupantCrashed = "event.replica.occupant.crashed";
            public const string RestartScheduled = "event.replica.restart.scheduled";
            public const string RestartAttempted = "event.replica.restart.attempted";
            public const string ReplacementScheduled = "event.replica.replacement.scheduled";
            public const string ReplacementMaterializing = "event.replica.replacement.materializing";
            public const string ReplacementMaterialized = "event.replica.replacement.materialized";
            public const string SlotLeftVacant = "event.replica.slot.leftVacant";
            public const string ReconciliationDeferred = "event.replica.reconciliation.deferred";
            public const string ReconciliationFailed = "event.replica.reconciliation.failed";
            public const string ReconciliationExhausted = "event.replica.reconciliation.exhausted";
        }

        public static class Configuration
        {
            public const string AppSettingsUpdated = "event.configuration.appSettings.updated";
            public const string EnvironmentVariablesUpdated = "event.configuration.environmentVariables.updated";
        }

        public static class Deployment
        {
            public const string Prefix = "event.deployment.";
            public const string ImageUpdated = "event.deployment.image.updated";
            public const string ReplicasUpdated = "event.deployment.replicas.updated";
            public const string Applying = "event.deployment.applying";
            public const string ServiceReconciling = "event.deployment.service.reconciling";
            public const string ServiceReconciled = "event.deployment.service.reconciled";
            public const string ReplicaMaterializing = "event.deployment.replica.materializing";
            public const string ReplicaMaterialized = "event.deployment.replica.materialized";
            public const string RoutingUpdating = "event.deployment.routing.updating";
            public const string RoutingUpdated = "event.deployment.routing.updated";
            public const string RollingBack = "event.deployment.rollback.running";
            public const string RolledBack = "event.deployment.rollback.completed";
            public const string RollbackFailed = "event.deployment.rollback.failed";
            public const string CleanupRunning = "event.deployment.cleanup.running";
            public const string CleanupCompleted = "event.deployment.cleanup.completed";
            public const string CleanupWarning = "event.deployment.cleanup.warning";
            public const string Applied = "event.deployment.applied";
            public const string Failed = "event.deployment.failed";
        }
    }

    public static string NormalizeEventTypeSegment(string value)
    {
        var builder = new StringBuilder(value.Trim().Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            builder.Append(
                char.IsAsciiLetterOrDigit(character) ||
                character is '.' or '-' or '_'
                    ? character
                    : '-');
        }

        return builder.Length == 0
            ? "custom"
            : builder.ToString();
    }
}
