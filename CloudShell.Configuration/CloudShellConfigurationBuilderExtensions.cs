using Microsoft.Extensions.Configuration;

namespace CloudShell.Configuration;

public static class CloudShellConfigurationBuilderExtensions
{
    public static IConfigurationBuilder AddCloudShellConfiguration(
        this IConfigurationBuilder builder)
    {
        builder.Add(new CloudShellConfigurationSource());
        return builder;
    }

    public static IConfigurationBuilder AddCloudShellConfiguration(
        this IConfigurationBuilder builder,
        Action<CloudShellConfigurationOptions> configure)
    {
        var options = new CloudShellConfigurationOptions();
        configure(options);
        builder.Add(new CloudShellConfigurationSource(options));
        return builder;
    }
}
