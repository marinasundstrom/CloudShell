using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.Providers;
using Microsoft.AspNetCore.Builder;

namespace CloudShell.Hosting;

public static class CloudShellCombinedHostApplicationBuilderExtensions
{
    public static IControlPlaneBuilder AddCloudShellControlPlaneApplication(
        this WebApplicationBuilder builder,
        Action<BuiltInResourceModelProviderOptions>? configureBuiltInResourceModelProviders = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var controlPlane = builder.AddCloudShellControlPlane();
        controlPlane.UseBuiltInResourceModelProviders(configureBuiltInResourceModelProviders);

        return controlPlane;
    }

    public static IControlPlaneBuilder AddCloudShell(
        this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var controlPlane = builder.AddCloudShellControlPlane();
        builder.AddCloudShellUi();

        return controlPlane;
    }
}
