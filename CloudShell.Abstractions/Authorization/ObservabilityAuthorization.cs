namespace CloudShell.Abstractions.Authorization;

public static class ObservabilityAuthorization
{
    public static IReadOnlyList<string> AnyReadPermissions { get; } =
    [
        CloudShellPermissions.Observability.Read,
        CloudShellPermissions.Observability.Logs.Read,
        CloudShellPermissions.Observability.Traces.Read,
        CloudShellPermissions.Observability.Metrics.Read
    ];

    public static IReadOnlyList<string> LogsReadPermissions { get; } =
    [
        CloudShellPermissions.Observability.Read,
        CloudShellPermissions.Observability.Logs.Read
    ];

    public static IReadOnlyList<string> TracesReadPermissions { get; } =
    [
        CloudShellPermissions.Observability.Read,
        CloudShellPermissions.Observability.Traces.Read
    ];

    public static IReadOnlyList<string> MetricsReadPermissions { get; } =
    [
        CloudShellPermissions.Observability.Read,
        CloudShellPermissions.Observability.Metrics.Read
    ];

    public static bool CanReadAnyObservability(this ICloudShellAuthorizationService authorization) =>
        HasAnyPermission(authorization, AnyReadPermissions);

    public static bool CanReadLogs(this ICloudShellAuthorizationService authorization) =>
        HasAnyPermission(authorization, LogsReadPermissions);

    public static bool CanReadTraces(this ICloudShellAuthorizationService authorization) =>
        HasAnyPermission(authorization, TracesReadPermissions);

    public static bool CanReadMetrics(this ICloudShellAuthorizationService authorization) =>
        HasAnyPermission(authorization, MetricsReadPermissions);

    public static bool HasAnyPermission(
        this ICloudShellAuthorizationService authorization,
        IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(permissions);

        return permissions.Any(authorization.HasPermission);
    }
}
