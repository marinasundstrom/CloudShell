using CloudShell.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Providers.Configuration;

public static class ConfigurationProviderServiceCollectionExtensions
{
    public static ICloudShellBuilder AddConfigurationProvider(
        this ICloudShellBuilder builder,
        Action<ConfigurationProviderOptions>? configure = null)
    {
        var options = new ConfigurationProviderOptions();
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);

        return builder.AddExtension<ConfigurationProviderExtension>();
    }
}
