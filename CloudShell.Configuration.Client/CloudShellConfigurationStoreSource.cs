using Microsoft.Extensions.Configuration;

namespace CloudShell.Configuration.Client;

internal sealed class CloudShellConfigurationStoreSource(
    CloudShellConfigurationStoreOptions? options = null) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new CloudShellConfigurationStoreProvider(options ?? new CloudShellConfigurationStoreOptions());
}
