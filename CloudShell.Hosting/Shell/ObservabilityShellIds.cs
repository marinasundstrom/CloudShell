using CoreShell;

namespace CloudShell.Hosting.Shell;

public static class ObservabilityShellIds
{
    public static readonly CoreShellModuleId Module =
        CoreShellModuleId.Create("cloudshell.observability");

    public static readonly CoreShellPageId OverviewPage =
        CoreShellPageId.Create("cloudshell.observability.overview");

    public static readonly CoreShellPageId LogsPage =
        CoreShellPageId.Create("cloudshell.observability.logs");

    public static readonly CoreShellPageId DependenciesPage =
        CoreShellPageId.Create("cloudshell.observability.dependencies");

    public static readonly CoreShellPageId ServiceMapPage =
        CoreShellPageId.Create("cloudshell.observability.service-map");

    public static readonly CoreShellPageId TracesPage =
        CoreShellPageId.Create("cloudshell.observability.traces");

    public static readonly CoreShellPageId MetricsPage =
        CoreShellPageId.Create("cloudshell.observability.metrics");

    public static readonly CoreShellMenuItemId OverviewMenuItem =
        CoreShellMenuItemId.Create(ShellIds.WorkspaceMenuGroup, "observability");

    public static readonly CoreShellMenuItemId LogsMenuItem =
        CoreShellMenuItemId.Create(ShellIds.WorkspaceMenuGroup, "observability.logs");

    public static readonly CoreShellMenuItemId DependenciesMenuItem =
        CoreShellMenuItemId.Create(ShellIds.WorkspaceMenuGroup, "observability.dependencies");

    public static readonly CoreShellMenuItemId ServiceMapMenuItem =
        CoreShellMenuItemId.Create(ShellIds.WorkspaceMenuGroup, "observability.service-map");

    public static readonly CoreShellMenuItemId TracesMenuItem =
        CoreShellMenuItemId.Create(ShellIds.WorkspaceMenuGroup, "observability.traces");

    public static readonly CoreShellMenuItemId MetricsMenuItem =
        CoreShellMenuItemId.Create(ShellIds.WorkspaceMenuGroup, "observability.metrics");
}
