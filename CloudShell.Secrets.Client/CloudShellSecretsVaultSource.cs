using Microsoft.Extensions.Configuration;

namespace CloudShell.Secrets.Client;

internal sealed class CloudShellSecretsVaultSource(
    CloudShellSecretsVaultOptions? options = null) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new CloudShellSecretsVaultProvider(options ?? new CloudShellSecretsVaultOptions());
}
