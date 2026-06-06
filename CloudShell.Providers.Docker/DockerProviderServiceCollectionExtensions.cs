using CloudShell.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Providers.Docker;

public static class DockerProviderServiceCollectionExtensions
{
    public static ICloudShellBuilder AddDockerProvider(
        this ICloudShellBuilder builder,
        Action<DockerProviderOptions>? configure = null)
    {
        AddDockerProviderCore(builder, configure);
        return builder.AddExtension<DockerProviderExtension>();
    }

    public static IControlPlaneBuilder AddDockerProvider(
        this IControlPlaneBuilder builder,
        Action<DockerProviderOptions>? configure = null)
    {
        AddDockerProviderCore(builder, configure);
        return builder.AddExtension<DockerProviderExtension>();
    }

    private static void AddDockerProviderCore(
        ICloudShellBuilder builder,
        Action<DockerProviderOptions>? configure)
    {
        var options = new DockerProviderOptions();
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);
    }
}
