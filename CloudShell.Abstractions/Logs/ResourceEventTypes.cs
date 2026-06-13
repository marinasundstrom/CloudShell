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
            public const string Run = "action.lifecycle.run";
            public const string Stop = "action.lifecycle.stop";
            public const string Pause = "action.lifecycle.pause";
            public const string Restart = "action.lifecycle.restart";
        }

        public static string ForAction(string actionId) =>
            actionId.Trim().ToLowerInvariant() switch
            {
                ResourceActionIds.Run => Lifecycle.Run,
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
        public static class Lifecycle
        {
            public const string Starting = "lifecycle.starting";
            public const string Started = "lifecycle.started";
            public const string StartFailed = "lifecycle.start.failed";
            public const string Stopping = "lifecycle.stopping";
            public const string Stopped = "lifecycle.stopped";
            public const string StopFailed = "lifecycle.stop.failed";
            public const string Pausing = "lifecycle.pausing";
            public const string Paused = "lifecycle.paused";
            public const string PauseFailed = "lifecycle.pause.failed";
            public const string Restarting = "lifecycle.restarting";
            public const string Restarted = "lifecycle.restarted";
            public const string RestartFailed = "lifecycle.restart.failed";
        }
    }

    private static string NormalizeEventTypeSegment(string value)
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
