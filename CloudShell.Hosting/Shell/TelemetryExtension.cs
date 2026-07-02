using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Extensions;
using CloudShell.Hosting.Components.Pages.Logs;
using CoreShell;

namespace CloudShell.Hosting.Shell;

public sealed class TelemetryExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.telemetry",
        "Telemetry",
        "Shared telemetry views for resources, providers, and extension-owned artifacts.",
        "0.1.0",
        ["telemetry.views", "logs.views", "logs.sources", "traces.views", "metrics.views"],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.AddCoreShellModule(
            TelemetryShellIds.Module,
            module =>
            {
                module.AddPage(
                    TelemetryShellIds.OverviewPage,
                    "Telemetry",
                    "/telemetry",
                    authorization: CoreShellAuthorizationRequirements.FromAnyPermissions(ObservabilityAuthorization.AnyReadPermissions));
                module.AddPage(
                    TelemetryShellIds.LogsPage,
                    "Logs",
                    "/logs",
                    authorization: CoreShellAuthorizationRequirements.FromAnyPermissions(ObservabilityAuthorization.LogsReadPermissions));
                module.AddPage(
                    TelemetryShellIds.DependenciesPage,
                    "Dependencies",
                    "/telemetry/dependencies",
                    authorization: CoreShellAuthorizationRequirements.FromAnyPermissions(ObservabilityAuthorization.TracesReadPermissions));
                module.AddPage(
                    TelemetryShellIds.ServiceMapPage,
                    "Service map",
                    "/telemetry/service-map",
                    authorization: CoreShellAuthorizationRequirements.FromAnyPermissions(ObservabilityAuthorization.TracesReadPermissions));
                module.AddPage(
                    TelemetryShellIds.TracesPage,
                    "Traces",
                    "/telemetry/traces",
                    authorization: CoreShellAuthorizationRequirements.FromAnyPermissions(ObservabilityAuthorization.TracesReadPermissions));
                module.AddPage(
                    TelemetryShellIds.MetricsPage,
                    "Metrics",
                    "/telemetry/metrics",
                    authorization: CoreShellAuthorizationRequirements.FromAnyPermissions(ObservabilityAuthorization.MetricsReadPermissions));

                var workspaceMenu = module
                    .AddMenu(ShellIds.MainMenu, "Main")
                    .AddGroup(ShellIds.WorkspaceMenuGroup, "Workspace", 10);

                workspaceMenu
                    .AddItem(TelemetryShellIds.OverviewMenuItem, "Telemetry", 20)
                    .WithAttribute(CoreShellAttributeNames.Icon, "pulse")
                    .RequiresPermissions(ObservabilityAuthorization.AnyReadPermissions)
                    .Target(TelemetryShellIds.OverviewPage);
                workspaceMenu
                    .AddItem(TelemetryShellIds.LogsMenuItem, "Logs", 21)
                    .WithAttribute(CoreShellAttributeNames.Icon, "document")
                    .WithParent(TelemetryShellIds.OverviewMenuItem)
                    .RequiresPermissions(ObservabilityAuthorization.LogsReadPermissions)
                    .Target(TelemetryShellIds.LogsPage);
                workspaceMenu
                    .AddItem(TelemetryShellIds.DependenciesMenuItem, "Dependencies", 22)
                    .WithAttribute(CoreShellAttributeNames.Icon, "dependencies")
                    .WithParent(TelemetryShellIds.OverviewMenuItem)
                    .RequiresPermissions(ObservabilityAuthorization.TracesReadPermissions)
                    .Target(TelemetryShellIds.DependenciesPage);
                workspaceMenu
                    .AddItem(TelemetryShellIds.ServiceMapMenuItem, "Service map", 23)
                    .WithAttribute(CoreShellAttributeNames.Icon, "service-map")
                    .WithParent(TelemetryShellIds.OverviewMenuItem)
                    .RequiresPermissions(ObservabilityAuthorization.TracesReadPermissions)
                    .Target(TelemetryShellIds.ServiceMapPage);
                workspaceMenu
                    .AddItem(TelemetryShellIds.TracesMenuItem, "Traces", 24)
                    .WithAttribute(CoreShellAttributeNames.Icon, "traces")
                    .WithParent(TelemetryShellIds.OverviewMenuItem)
                    .RequiresPermissions(ObservabilityAuthorization.TracesReadPermissions)
                    .Target(TelemetryShellIds.TracesPage);
                workspaceMenu
                    .AddItem(TelemetryShellIds.MetricsMenuItem, "Metrics", 25)
                    .WithAttribute(CoreShellAttributeNames.Icon, "metrics")
                    .WithParent(TelemetryShellIds.OverviewMenuItem)
                    .RequiresPermissions(ObservabilityAuthorization.MetricsReadPermissions)
                    .Target(TelemetryShellIds.MetricsPage);
            });

        builder
            .RegisterView<Components.Pages.Observability.Telemetry>(TelemetryViews.Overview)
            .RegisterView<Components.Pages.Logs.Logs>(TelemetryViews.Logs)
            .RegisterView<Components.Pages.Observability.DependencyGraph>(TelemetryViews.Dependencies)
            .RegisterView<Components.Pages.Observability.ServiceMap>(TelemetryViews.ServiceMap)
            .RegisterView<Components.Pages.Observability.Traces>(TelemetryViews.Traces)
            .RegisterView<Components.Pages.Observability.Metrics>(TelemetryViews.Metrics);
    }
}
