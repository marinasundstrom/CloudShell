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

    public static IControlPlaneBuilder AddCloudShellControlPlaneApplication(
        this WebApplicationBuilder builder,
        Action<BuiltInResourceModelProviderOptions>? configureBuiltInResourceModelProviders,
        Action<IControlPlaneBuilder> configureControlPlane)
    {
        ArgumentNullException.ThrowIfNull(configureControlPlane);

        var controlPlane = builder.AddCloudShellControlPlaneApplication(
            configureBuiltInResourceModelProviders);
        configureControlPlane(controlPlane);

        return controlPlane;
    }
}
