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

    public static InMemoryIdentityProviderDeclaration GetIdentityProvider(
        this IResourceGraphBuilder builder,
        string? providerId = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var declarations = GetOrAddDeclarationStore(builder.Services);
        var setup = GetOrAddSetupOptions(builder.Services);
        var resolvedProviderId = string.IsNullOrWhiteSpace(providerId)
            ? declarations.DefaultIdentityProviderId
            : providerId.Trim();
        if (string.IsNullOrWhiteSpace(resolvedProviderId))
        {
            throw new InvalidOperationException("No default identity provider is configured.");
        }

        var provider = declarations
            .GetIdentityProviders()
            .SingleOrDefault(provider => string.Equals(
                provider.Id,
                resolvedProviderId,
                StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            throw new InvalidOperationException(
                $"Identity provider '{resolvedProviderId}' is not configured.");
        }

        return new InMemoryIdentityProviderDeclaration(provider, setup);
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

public sealed class InMemoryIdentityProviderDeclaration(
    ResourceIdentityProviderDefinition provider,
    InMemoryIdentitySetupOptions setup)
{
    public ResourceIdentityProviderDefinition Provider { get; } = provider;

    public ResourcePrincipalReference GetUser(string userName)
    {
        if (!setup.Users.TryGetValue(userName, out var user))
        {
            throw new InvalidOperationException(
                $"Built-in in-memory user '{userName}' is not configured.");
        }

        return user.ToPrincipal(Provider.Id);
    }

    public static implicit operator ResourceIdentityProviderDefinition(
        InMemoryIdentityProviderDeclaration declaration) =>
        declaration.Provider;
}
