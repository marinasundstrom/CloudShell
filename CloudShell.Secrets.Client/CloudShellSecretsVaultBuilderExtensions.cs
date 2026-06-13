using Microsoft.Extensions.Configuration;

namespace CloudShell.Secrets.Client;

/// <summary>
/// Registers CloudShell Secrets Vault values as a
/// <see cref="IConfigurationBuilder"/> source.
/// </summary>
/// <remarks>
/// Public preview API. The provider authenticates with
/// <see cref="CloudShell.Client.Authentication.CloudShellResourceCredential"/>
/// and reads secrets from the protected Secrets Vault service.
/// </remarks>
public static class CloudShellSecretsVaultBuilderExtensions
{
    public static IConfigurationBuilder AddCloudShellSecretsVault(
        this IConfigurationBuilder builder)
    {
        builder.Add(new CloudShellSecretsVaultSource());
        return builder;
    }

    public static IConfigurationBuilder AddCloudShellSecretsVault(
        this IConfigurationBuilder builder,
        Action<CloudShellSecretsVaultOptions> configure)
    {
        var options = new CloudShellSecretsVaultOptions();
        configure(options);
        builder.Add(new CloudShellSecretsVaultSource(options));
        return builder;
    }
}
