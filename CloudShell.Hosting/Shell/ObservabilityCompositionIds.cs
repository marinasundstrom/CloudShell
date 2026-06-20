using CloudShell.UI.Composition;

namespace CloudShell.Hosting.Shell;

public static class ObservabilityCompositionIds
{
    public static readonly CompositionModuleId Module =
        CompositionModuleId.Create("cloudshell.observability");

    public static readonly PageId OverviewPage =
        PageId.Create("cloudshell.observability.overview");

    public static readonly PageId LogsPage =
        PageId.Create("cloudshell.observability.logs");

    public static readonly PageId DependenciesPage =
        PageId.Create("cloudshell.observability.dependencies");

    public static readonly PageId ServiceMapPage =
        PageId.Create("cloudshell.observability.service-map");

    public static readonly PageId TracesPage =
        PageId.Create("cloudshell.observability.traces");

    public static readonly PageId MetricsPage =
        PageId.Create("cloudshell.observability.metrics");

    public static readonly MenuItemId OverviewMenuItem =
        MenuItemId.Create(ShellCompositionIds.WorkspaceMenuGroup, "observability");

    public static readonly MenuItemId LogsMenuItem =
        MenuItemId.Create(ShellCompositionIds.WorkspaceMenuGroup, "observability.logs");

    public static readonly MenuItemId DependenciesMenuItem =
        MenuItemId.Create(ShellCompositionIds.WorkspaceMenuGroup, "observability.dependencies");

    public static readonly MenuItemId ServiceMapMenuItem =
        MenuItemId.Create(ShellCompositionIds.WorkspaceMenuGroup, "observability.service-map");

    public static readonly MenuItemId TracesMenuItem =
        MenuItemId.Create(ShellCompositionIds.WorkspaceMenuGroup, "observability.traces");

    public static readonly MenuItemId MetricsMenuItem =
        MenuItemId.Create(ShellCompositionIds.WorkspaceMenuGroup, "observability.metrics");
}
