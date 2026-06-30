using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.Hosting;
using Microsoft.AspNetCore.Builder;

namespace CloudShell.Hosting;

public static class CloudShellCombinedHostApplicationBuilderExtensions
{
    public static IControlPlaneBuilder AddCloudShell(
        this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var controlPlane = builder.AddCloudShellControlPlane();
        builder.AddCloudShellUi();

        return controlPlane;
    }
}
