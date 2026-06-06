using CloudShell.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Providers.Applications;

public static class ApplicationProviderServiceCollectionExtensions
{
    public static ICloudShellBuilder AddApplicationProvider(
        this ICloudShellBuilder builder,
        Action<ApplicationProviderOptions>? configure = null)
    {
        var options = new ApplicationProviderOptions();
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);

        return builder.AddExtension<ApplicationProviderExtension>();
    }
}
