using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Hosting;

public static class InMemoryIdentityControlPlaneBuilderExtensions
{
    public static IControlPlaneBuilder ConfigureInMemoryIdentity(
        this IControlPlaneBuilder builder,
        Action<InMemoryIdentitySetupOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var setup = GetOrAddSetupOptions(builder.Services);
        setup.IsConfigured = true;
        configure(setup);
        if (setup.UseAspNetCoreIdentityStore)
        {
            UseInMemoryAspNetCoreIdentityStore(builder.Services);
        }

        GetOrAddDeclarationStore(builder.Services)
            .AddIdentityProvider(
                new ResourceIdentityProviderDefinition(
                    setup.ProviderId,
                    setup.ProviderName,
                    ResourceIdentityProviderKind.BuiltIn),
                setup.UseAsDefaultProvider);

        return builder;
    }

    private static InMemoryIdentitySetupOptions GetOrAddSetupOptions(IServiceCollection services)
    {
        var setup = services
            .Where(descriptor => descriptor.ServiceType == typeof(InMemoryIdentitySetupOptions))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<InMemoryIdentitySetupOptions>()
            .SingleOrDefault();
        if (setup is not null)
        {
            return setup;
        }

        setup = new InMemoryIdentitySetupOptions();
        services.AddSingleton(setup);
        return setup;
    }

    private static void UseInMemoryAspNetCoreIdentityStore(IServiceCollection services)
    {
        services.RemoveAll<IUserStore<IdentityUser>>();
        services.RemoveAll<IRoleStore<IdentityRole>>();
        services.TryAddSingleton<InMemoryIdentityStore>();
        services.AddScoped<IUserStore<IdentityUser>>(
            serviceProvider => serviceProvider.GetRequiredService<InMemoryIdentityStore>());
        services.AddScoped<IRoleStore<IdentityRole>>(
            serviceProvider => serviceProvider.GetRequiredService<InMemoryIdentityStore>());
    }

    private static ResourceDeclarationStore GetOrAddDeclarationStore(IServiceCollection services)
    {
        var declarations = services
            .Where(descriptor => descriptor.ServiceType == typeof(ResourceDeclarationStore))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ResourceDeclarationStore>()
            .SingleOrDefault();
        if (declarations is not null)
        {
            return declarations;
        }

        declarations = new ResourceDeclarationStore();
        services.AddSingleton(declarations);
        return declarations;
    }
}
