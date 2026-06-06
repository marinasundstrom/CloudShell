using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Providers.Configuration;

public static class ConfigurationProviderServiceCollectionExtensions
{
    public static ICloudShellBuilder AddConfigurationProvider(
        this ICloudShellBuilder builder,
        Action<ConfigurationProviderOptions>? configure = null)
    {
        AddConfigurationProviderCore(builder, configure);
        return builder.AddExtension<ConfigurationProviderExtension>();
    }

    public static IControlPlaneBuilder AddConfigurationProvider(
        this IControlPlaneBuilder builder,
        Action<ConfigurationProviderOptions>? configure = null)
    {
        AddConfigurationProviderCore(builder, configure);
        return builder.AddExtension<ConfigurationProviderExtension>();
    }

    private static void AddConfigurationProviderCore(
        ICloudShellBuilder builder,
        Action<ConfigurationProviderOptions>? configure)
    {
        var options = builder.Services.GetOrAddConfigurationProviderOptions();
        configure?.Invoke(options);
    }

    public static ICloudShellResourceBuilder AddConfigurationStore(
        this ICloudShellResourceDeclarationBuilder builder,
        string id,
        string name,
        IReadOnlyList<ConfigurationEntry>? entries = null,
        string? accessToken = null)
    {
        var definition = new ConfigurationStoreDefinition(
            id,
            name,
            entries,
            accessToken);
        var declared = new DeclaredConfigurationStore(definition);

        builder.Services
            .GetOrAddConfigurationProviderOptions()
            .DeclaredStores
            .Add(declared);

        return builder.Declare(
            "configuration",
            id,
            onChanged: declaration =>
            {
                declared.Persist = declaration.Persistence == ResourceDeclarationPersistence.Persisted;
                declared.OverwritePersistedState = declaration.OverwritePersistedState;
            });
    }

    private static ConfigurationProviderOptions GetOrAddConfigurationProviderOptions(
        this IServiceCollection services)
    {
        var options = services
            .Where(descriptor => descriptor.ServiceType == typeof(ConfigurationProviderOptions))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ConfigurationProviderOptions>()
            .SingleOrDefault();

        if (options is not null)
        {
            return options;
        }

        options = new ConfigurationProviderOptions();
        services.AddSingleton(options);
        return options;
    }
}
