using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.Client;

public static class ControlPlaneClientServiceCollectionExtensions
{
    public static IServiceCollection AddRemoteControlPlane(
        this IServiceCollection services,
        Action<RemoteControlPlaneOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services
            .AddOptions<RemoteControlPlaneOptions>()
            .Configure(configure);

        AddRemoteControlPlaneServices(services, (serviceProvider, client) =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<RemoteControlPlaneOptions>>()
                .Value;
            client.BaseAddress = options.BaseAddress ??
                throw new InvalidOperationException(
                    $"{RemoteControlPlaneOptions.SectionName}:BaseAddress must be configured.");
        });

        return services;
    }

    public static IServiceCollection AddRemoteControlPlane(
        this IServiceCollection services,
        Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);

        AddRemoteControlPlaneServices(services, (_, client) =>
        {
            client.BaseAddress = baseAddress;
        });

        return services;
    }

    private static void AddRemoteControlPlaneServices(
        IServiceCollection services,
        Action<IServiceProvider, HttpClient> configureClient)
    {
        services.AddHttpClient("CloudShell.ControlPlane.Auth");
        services.AddTransient<ControlPlaneAuthenticationHandler>();
        services.AddTransient<ControlPlaneCredential>(serviceProvider =>
        {
            var options = serviceProvider
                .GetRequiredService<IOptions<RemoteControlPlaneOptions>>()
                .Value;
            return options.Credential.Mode.ToUpperInvariant() switch
            {
                "NONE" => new EmptyControlPlaneCredential(),
                "STATICBEARER" => new StaticBearerControlPlaneCredential(
                    options.Credential.BearerToken ??
                    throw new InvalidOperationException(
                        $"{RemoteControlPlaneOptions.SectionName}:Credential:BearerToken must be configured.")),
                "CLIENTCREDENTIALS" => new ClientCredentialsControlPlaneCredential(
                    serviceProvider.GetRequiredService<IHttpClientFactory>(),
                    serviceProvider.GetRequiredService<IOptions<RemoteControlPlaneOptions>>()),
                _ => throw new InvalidOperationException(
                    $"Unsupported Control Plane credential mode '{options.Credential.Mode}'.")
            };
        });

        services
            .AddHttpClient<IControlPlane, RemoteControlPlane>((serviceProvider, client) =>
            {
                configureClient(serviceProvider, client);
            })
            .AddHttpMessageHandler<ControlPlaneAuthenticationHandler>();

        services
            .AddHttpClient<ICloudShellControlPlaneUserSettingsProvider, RemoteCloudShellUserSettingsProvider>(
                (serviceProvider, client) =>
                {
                    configureClient(serviceProvider, client);
                })
            .AddHttpMessageHandler<ControlPlaneAuthenticationHandler>();

        services.AddScoped<IResourceManager>(
            serviceProvider => serviceProvider.GetRequiredService<IControlPlane>());
        services.AddScoped<IResourceTemplateManager>(
            serviceProvider => serviceProvider.GetRequiredService<IControlPlane>());
        services.AddScoped<ILogManager>(
            serviceProvider => serviceProvider.GetRequiredService<IControlPlane>());
        services.AddScoped<ITraceManager>(
            serviceProvider => serviceProvider.GetRequiredService<IControlPlane>());
    }
}
