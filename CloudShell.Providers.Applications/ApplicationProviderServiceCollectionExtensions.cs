using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Providers.Applications;

public static class ApplicationProviderServiceCollectionExtensions
{
    public static ICloudShellBuilder AddApplicationProvider(
        this ICloudShellBuilder builder,
        Action<ApplicationProviderOptions>? configure = null)
    {
        AddApplicationProviderCore(builder, configure);
        return builder.AddExtension<ApplicationProviderExtension>();
    }

    public static IControlPlaneBuilder AddApplicationProvider(
        this IControlPlaneBuilder builder,
        Action<ApplicationProviderOptions>? configure = null)
    {
        AddApplicationProviderCore(builder, configure);
        return builder.AddExtension<ApplicationProviderExtension>();
    }

    private static void AddApplicationProviderCore(
        ICloudShellBuilder builder,
        Action<ApplicationProviderOptions>? configure)
    {
        var options = builder.Services.GetOrAddApplicationProviderOptions();
        configure?.Invoke(options);
    }

    public static ICloudShellResourceBuilder AddExecutableApplication(
        this ICloudShellResourceDeclarationBuilder builder,
        string id,
        string name,
        string executablePath,
        string? arguments = null,
        string? workingDirectory = null,
        string? endpoint = null,
        IReadOnlyList<EnvironmentVariableAssignment>? environmentVariables = null,
        ApplicationLifetime lifetime = ApplicationLifetime.Detached)
    {
        var definition = new ApplicationResourceDefinition(
            id,
            name,
            executablePath,
            arguments,
            workingDirectory,
            endpoint,
            environmentVariables,
            lifetime);
        var declared = new DeclaredApplicationResource(definition);

        builder.Services
            .GetOrAddApplicationProviderOptions()
            .DeclaredApplications
            .Add(declared);

        return builder.Declare(
            "applications",
            id,
            onChanged: declaration =>
            {
                declared.Definition = declared.Definition with
                {
                    DependsOn = declaration.DependsOn
                };
                declared.Persist = declaration.Persistence == ResourceDeclarationPersistence.Persisted;
                declared.OverwritePersistedState = declaration.OverwritePersistedState;
            });
    }

    private static ApplicationProviderOptions GetOrAddApplicationProviderOptions(
        this IServiceCollection services)
    {
        var options = services
            .Where(descriptor => descriptor.ServiceType == typeof(ApplicationProviderOptions))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ApplicationProviderOptions>()
            .SingleOrDefault();

        if (options is not null)
        {
            return options;
        }

        options = new ApplicationProviderOptions();
        services.AddSingleton(options);
        return options;
    }
}
