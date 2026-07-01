using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Extensions;
using CloudShell.Hosting.Components.Pages.Logs;
using CoreShell;

namespace CloudShell.Hosting.Shell;

public sealed class ObservabilityExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.observability",
        "Observability",
        "Shared telemetry views for resources, providers, and extension-owned artifacts.",
        "0.1.0",
        ["observability.views", "logs.views", "logs.sources", "traces.views", "metrics.views"],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.AddCoreShellModule(
            ObservabilityShellIds.Module,
            module =>
            {
                module.AddPage(
                    ObservabilityShellIds.OverviewPage,
                    "Observability",
                    "/observability",
                    authorization: CoreShellAuthorizationRequirements.FromAnyPermissions(ObservabilityAuthorization.AnyReadPermissions));
                module.AddPage(
                    ObservabilityShellIds.LogsPage,
                    "Logs",
                    "/logs",
                    authorization: CoreShellAuthorizationRequirements.FromAnyPermissions(ObservabilityAuthorization.LogsReadPermissions));
                module.AddPage(
                    ObservabilityShellIds.DependenciesPage,
                    "Dependencies",
                    "/observability/dependencies",
                    authorization: CoreShellAuthorizationRequirements.FromAnyPermissions(ObservabilityAuthorization.TracesReadPermissions));
                module.AddPage(
                    ObservabilityShellIds.ServiceMapPage,
                    "Service map",
                    "/observability/service-map",
                    authorization: CoreShellAuthorizationRequirements.FromAnyPermissions(ObservabilityAuthorization.TracesReadPermissions));
                module.AddPage(
                    ObservabilityShellIds.TracesPage,
                    "Traces",
                    "/observability/traces",
                    authorization: CoreShellAuthorizationRequirements.FromAnyPermissions(ObservabilityAuthorization.TracesReadPermissions));
                module.AddPage(
                    ObservabilityShellIds.MetricsPage,
                    "Metrics",
                    "/observability/metrics",
                    authorization: CoreShellAuthorizationRequirements.FromAnyPermissions(ObservabilityAuthorization.MetricsReadPermissions));

                var workspaceMenu = module
                    .AddMenu(ShellIds.MainMenu, "Main")
                    .AddGroup(ShellIds.WorkspaceMenuGroup, "Workspace", 10);

                workspaceMenu
                    .AddItem(ObservabilityShellIds.OverviewMenuItem, "Observability", 20)
                    .WithAttribute(CoreShellAttributeNames.Icon, "pulse")
                    .RequiresPermissions(ObservabilityAuthorization.AnyReadPermissions)
                    .Target(ObservabilityShellIds.OverviewPage);
                workspaceMenu
                    .AddItem(ObservabilityShellIds.LogsMenuItem, "Logs", 21)
                    .WithAttribute(CoreShellAttributeNames.Icon, "document")
                    .WithParent(ObservabilityShellIds.OverviewMenuItem)
                    .RequiresPermissions(ObservabilityAuthorization.LogsReadPermissions)
                    .Target(ObservabilityShellIds.LogsPage);
                workspaceMenu
                    .AddItem(ObservabilityShellIds.DependenciesMenuItem, "Dependencies", 22)
                    .WithAttribute(CoreShellAttributeNames.Icon, "dependencies")
                    .WithParent(ObservabilityShellIds.OverviewMenuItem)
                    .RequiresPermissions(ObservabilityAuthorization.TracesReadPermissions)
                    .Target(ObservabilityShellIds.DependenciesPage);
                workspaceMenu
                    .AddItem(ObservabilityShellIds.ServiceMapMenuItem, "Service map", 23)
                    .WithAttribute(CoreShellAttributeNames.Icon, "service-map")
                    .WithParent(ObservabilityShellIds.OverviewMenuItem)
                    .RequiresPermissions(ObservabilityAuthorization.TracesReadPermissions)
                    .Target(ObservabilityShellIds.ServiceMapPage);
                workspaceMenu
                    .AddItem(ObservabilityShellIds.TracesMenuItem, "Traces", 24)
                    .WithAttribute(CoreShellAttributeNames.Icon, "traces")
                    .WithParent(ObservabilityShellIds.OverviewMenuItem)
                    .RequiresPermissions(ObservabilityAuthorization.TracesReadPermissions)
                    .Target(ObservabilityShellIds.TracesPage);
                workspaceMenu
                    .AddItem(ObservabilityShellIds.MetricsMenuItem, "Metrics", 25)
                    .WithAttribute(CoreShellAttributeNames.Icon, "metrics")
                    .WithParent(ObservabilityShellIds.OverviewMenuItem)
                    .RequiresPermissions(ObservabilityAuthorization.MetricsReadPermissions)
                    .Target(ObservabilityShellIds.MetricsPage);
            });

        builder
            .RegisterView<Components.Pages.Observability.Observability>(ObservabilityViews.Overview)
            .RegisterView<Components.Pages.Logs.Logs>(ObservabilityViews.Logs)
            .RegisterView<Components.Pages.Observability.DependencyGraph>(ObservabilityViews.Dependencies)
            .RegisterView<Components.Pages.Observability.ServiceMap>(ObservabilityViews.ServiceMap)
            .RegisterView<Components.Pages.Observability.Traces>(ObservabilityViews.Traces)
            .RegisterView<Components.Pages.Observability.Metrics>(ObservabilityViews.Metrics);
    }
}
