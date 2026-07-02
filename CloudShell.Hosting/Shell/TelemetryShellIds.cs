using CoreShell;

namespace CloudShell.Hosting.Shell;

public static class TelemetryShellIds
{
    public static readonly CoreShellModuleId Module =
        CoreShellModuleId.Create("cloudshell.telemetry");

    public static readonly CoreShellPageId OverviewPage =
        CoreShellPageId.Create("cloudshell.telemetry.overview");

    public static readonly CoreShellPageId LogsPage =
        CoreShellPageId.Create("cloudshell.telemetry.logs");

    public static readonly CoreShellPageId DependenciesPage =
        CoreShellPageId.Create("cloudshell.telemetry.dependencies");

    public static readonly CoreShellPageId ServiceMapPage =
        CoreShellPageId.Create("cloudshell.telemetry.service-map");

    public static readonly CoreShellPageId TracesPage =
        CoreShellPageId.Create("cloudshell.telemetry.traces");

    public static readonly CoreShellPageId MetricsPage =
        CoreShellPageId.Create("cloudshell.telemetry.metrics");

    public static readonly CoreShellMenuItemId OverviewMenuItem =
        CoreShellMenuItemId.Create(ShellIds.WorkspaceMenuGroup, "telemetry");

    public static readonly CoreShellMenuItemId LogsMenuItem =
        CoreShellMenuItemId.Create(ShellIds.WorkspaceMenuGroup, "telemetry.logs");

    public static readonly CoreShellMenuItemId DependenciesMenuItem =
        CoreShellMenuItemId.Create(ShellIds.WorkspaceMenuGroup, "telemetry.dependencies");

    public static readonly CoreShellMenuItemId ServiceMapMenuItem =
        CoreShellMenuItemId.Create(ShellIds.WorkspaceMenuGroup, "telemetry.service-map");

    public static readonly CoreShellMenuItemId TracesMenuItem =
        CoreShellMenuItemId.Create(ShellIds.WorkspaceMenuGroup, "telemetry.traces");

    public static readonly CoreShellMenuItemId MetricsMenuItem =
        CoreShellMenuItemId.Create(ShellIds.WorkspaceMenuGroup, "telemetry.metrics");
}
