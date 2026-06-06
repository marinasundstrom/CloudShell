using CloudShell.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Providers.Docker;

public static class DockerProviderServiceCollectionExtensions
{
    public static ICloudShellBuilder AddDockerProvider(
        this ICloudShellBuilder builder,
        Action<DockerProviderOptions>? configure = null)
    {
        var options = new DockerProviderOptions();
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);

        return builder.AddExtension<DockerProviderExtension>();
    }
}
