using Microsoft.Extensions.Configuration;

namespace CloudShell.Configuration;

internal sealed class CloudShellConfigurationSource(
    CloudShellConfigurationOptions? options = null) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new CloudShellConfigurationProvider(options ?? new CloudShellConfigurationOptions());
}
