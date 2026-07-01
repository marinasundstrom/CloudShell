using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Extensions;
using CloudShell.Hosting.Components.Pages.Logs;
using CoreShell.Composition;

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
        builder.AddCompositionModule(
            ObservabilityCompositionIds.Module,
            composition =>
            {
                composition.AddPage(
                    ObservabilityCompositionIds.OverviewPage,
                    "Observability",
                    "/observability",
                    authorization: CompositionAuthorizationRequirements.FromAnyPermissions(ObservabilityAuthorization.AnyReadPermissions));
                composition.AddPage(
                    ObservabilityCompositionIds.LogsPage,
                    "Logs",
                    "/logs",
                    authorization: CompositionAuthorizationRequirements.FromAnyPermissions(ObservabilityAuthorization.LogsReadPermissions));
                composition.AddPage(
                    ObservabilityCompositionIds.DependenciesPage,
                    "Dependencies",
                    "/observability/dependencies",
                    authorization: CompositionAuthorizationRequirements.FromAnyPermissions(ObservabilityAuthorization.TracesReadPermissions));
                composition.AddPage(
                    ObservabilityCompositionIds.ServiceMapPage,
                    "Service map",
                    "/observability/service-map",
                    authorization: CompositionAuthorizationRequirements.FromAnyPermissions(ObservabilityAuthorization.TracesReadPermissions));
                composition.AddPage(
                    ObservabilityCompositionIds.TracesPage,
                    "Traces",
                    "/observability/traces",
                    authorization: CompositionAuthorizationRequirements.FromAnyPermissions(ObservabilityAuthorization.TracesReadPermissions));
                composition.AddPage(
                    ObservabilityCompositionIds.MetricsPage,
                    "Metrics",
                    "/observability/metrics",
                    authorization: CompositionAuthorizationRequirements.FromAnyPermissions(ObservabilityAuthorization.MetricsReadPermissions));

                var workspaceMenu = composition
                    .GetMenu(ShellCompositionIds.MainMenu)
                    .AddGroup(ShellCompositionIds.WorkspaceMenuGroup, "Workspace", 10);

                workspaceMenu
                    .AddItem(ObservabilityCompositionIds.OverviewMenuItem, "Observability", 20)
                    .WithAttribute(CompositionAttributeNames.Icon, "pulse")
                    .RequiresPermissions(ObservabilityAuthorization.AnyReadPermissions)
                    .Target(ObservabilityCompositionIds.OverviewPage);
                workspaceMenu
                    .AddItem(ObservabilityCompositionIds.LogsMenuItem, "Logs", 21)
                    .WithAttribute(CompositionAttributeNames.Icon, "document")
                    .WithParent(ObservabilityCompositionIds.OverviewMenuItem)
                    .RequiresPermissions(ObservabilityAuthorization.LogsReadPermissions)
                    .Target(ObservabilityCompositionIds.LogsPage);
                workspaceMenu
                    .AddItem(ObservabilityCompositionIds.DependenciesMenuItem, "Dependencies", 22)
                    .WithAttribute(CompositionAttributeNames.Icon, "dependencies")
                    .WithParent(ObservabilityCompositionIds.OverviewMenuItem)
                    .RequiresPermissions(ObservabilityAuthorization.TracesReadPermissions)
                    .Target(ObservabilityCompositionIds.DependenciesPage);
                workspaceMenu
                    .AddItem(ObservabilityCompositionIds.ServiceMapMenuItem, "Service map", 23)
                    .WithAttribute(CompositionAttributeNames.Icon, "service-map")
                    .WithParent(ObservabilityCompositionIds.OverviewMenuItem)
                    .RequiresPermissions(ObservabilityAuthorization.TracesReadPermissions)
                    .Target(ObservabilityCompositionIds.ServiceMapPage);
                workspaceMenu
                    .AddItem(ObservabilityCompositionIds.TracesMenuItem, "Traces", 24)
                    .WithAttribute(CompositionAttributeNames.Icon, "traces")
                    .WithParent(ObservabilityCompositionIds.OverviewMenuItem)
                    .RequiresPermissions(ObservabilityAuthorization.TracesReadPermissions)
                    .Target(ObservabilityCompositionIds.TracesPage);
                workspaceMenu
                    .AddItem(ObservabilityCompositionIds.MetricsMenuItem, "Metrics", 25)
                    .WithAttribute(CompositionAttributeNames.Icon, "metrics")
                    .WithParent(ObservabilityCompositionIds.OverviewMenuItem)
                    .RequiresPermissions(ObservabilityAuthorization.MetricsReadPermissions)
                    .Target(ObservabilityCompositionIds.MetricsPage);
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
