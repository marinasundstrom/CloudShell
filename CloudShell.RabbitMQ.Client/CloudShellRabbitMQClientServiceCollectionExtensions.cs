using CloudShell.Client.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.RabbitMQ.Client;

public static class CloudShellRabbitMQClientServiceCollectionExtensions
{
    public static IServiceCollection AddCloudShellRabbitMQClient(
        this IServiceCollection services,
        Action<CloudShellRabbitMQClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new CloudShellRabbitMQClientOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddScoped<ICloudShellRabbitMQCredentialResolver>(_ =>
        {
            var credential = options.Credential ?? new DefaultCloudShellResourceCredential();
            if (options.CredentialEndpoint is { } credentialEndpoint)
            {
                return new CloudShellRabbitMQCredentialResolver(
                    credentialEndpoint,
                    credential,
                    options.Scopes);
            }

            return CloudShellRabbitMQCredentialResolver.FromEnvironment(
                credential,
                options.RabbitMQResourceName,
                options.Scopes);
        });
        services.AddScoped<CloudShellRabbitMQConnectionFactory>();

        return services;
    }
}
