using CloudShell.Abstractions.Logs;
using CloudShell.Hosting.Localization;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace CloudShell.Hosting.ResourceManager;

internal static class ResourceEventDisplayNames
{
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

        if (string.Equals(eventType, ResourceEventTypes.Events.Lifecycle.Stopping, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Stopping"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Lifecycle.Stopped, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Stopped"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Lifecycle.Pausing, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Pausing"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Lifecycle.Paused, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Paused"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Lifecycle.Restarting, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Restarting"].Value;
        }

        if (string.Equals(eventType, ResourceEventTypes.Events.Lifecycle.Restarted, StringComparison.OrdinalIgnoreCase))
        {
            return localizer["Restarted"].Value;
        }

        return FormatCustomEventDisplayName(eventType);
    }

    private static string FormatCustomEventDisplayName(string eventType)
    {
        var value = eventType;
        if (value.StartsWith(ResourceEventTypes.Actions.Prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[ResourceEventTypes.Actions.Prefix.Length..];
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
}
