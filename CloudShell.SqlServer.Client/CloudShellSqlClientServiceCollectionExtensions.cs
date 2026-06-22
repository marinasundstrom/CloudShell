using CloudShell.Client.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.SqlServer.Client;

public static class CloudShellSqlClientServiceCollectionExtensions
{
    public static IServiceCollection AddCloudShellSqlServerClient(
        this IServiceCollection services,
        Action<CloudShellSqlClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new CloudShellSqlClientOptions();
        configure?.Invoke(options);

        services.AddScoped<ICloudShellSqlCredentialResolver>(_ =>
        {
            var credential = options.Credential ?? new DefaultCloudShellResourceCredential();
            if (options.CredentialEndpoint is { } credentialEndpoint)
            {
                return new CloudShellSqlCredentialResolver(
                    credentialEndpoint,
                    credential,
                    options.Scopes);
            }

            return CloudShellSqlCredentialResolver.FromEnvironment(
                credential,
                options.SqlServerResourceName,
                options.Scopes);
        });
        services.AddScoped<CloudShellSqlConnectionFactory>();

        return services;
    }
}
