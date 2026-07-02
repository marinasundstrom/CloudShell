using System.Globalization;

namespace CloudShell.Hosting.ResourceManager;

public static class ResourceTimestampDisplay
{
    public static bool IsObserved(DateTimeOffset timestamp) =>
        timestamp != default &&
        timestamp != DateTimeOffset.MinValue &&
        timestamp != DateTimeOffset.UnixEpoch;

    public static string FormatDateTime(
        DateTimeOffset timestamp,
        IFormatProvider? culture = null,
        string format = "g",
        string fallback = "not observed") =>
        IsObserved(timestamp)
            ? timestamp.ToLocalTime().ToString(format, culture ?? CultureInfo.CurrentCulture)
            : fallback;

    public static string FormatTime(
        DateTimeOffset timestamp,
        IFormatProvider? culture = null,
        string fallback = "not observed") =>
        FormatDateTime(timestamp, culture, "HH:mm:ss", fallback);
}
