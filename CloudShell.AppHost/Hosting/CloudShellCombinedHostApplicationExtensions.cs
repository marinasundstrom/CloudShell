using CloudShell.ControlPlane.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Endpoints;

namespace CloudShell.Hosting;

public static class CloudShellCombinedHostApplicationExtensions
{
    public static async Task<WebApplication> UseCloudShellAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        await app.UseCloudShellControlPlaneAsync();
        await app.UseCloudShellUiAsync();

        return app;
    }

    public static RazorComponentsEndpointConventionBuilder MapCloudShell<TRootComponent>(
        this WebApplication app)
        where TRootComponent : IComponent
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapCloudShellControlPlane();
        return app.MapCloudShellUi<TRootComponent>();
    }
}
