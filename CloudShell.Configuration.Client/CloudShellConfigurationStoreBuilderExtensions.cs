using Microsoft.Extensions.Configuration;

namespace CloudShell.Configuration.Client;

/// <summary>
/// Registers CloudShell Configuration Store entries as a
/// <see cref="IConfigurationBuilder"/> source.
/// </summary>
/// <remarks>
/// Public preview API. The provider authenticates with
/// <see cref="CloudShell.Client.Authentication.CloudShellResourceCredential"/>
/// and reads entries from the protected Configuration Store service.
/// </remarks>
public static class CloudShellConfigurationStoreBuilderExtensions
{
    public static IConfigurationBuilder AddCloudShellConfigurationStore(
        this IConfigurationBuilder builder)
    {
        builder.Add(new CloudShellConfigurationStoreSource());
        return builder;
    }

    public static IConfigurationBuilder AddCloudShellConfigurationStore(
        this IConfigurationBuilder builder,
        Action<CloudShellConfigurationStoreOptions> configure)
    {
        var options = new CloudShellConfigurationStoreOptions();
        configure(options);
        builder.Add(new CloudShellConfigurationStoreSource(options));
        return builder;
    }
}
