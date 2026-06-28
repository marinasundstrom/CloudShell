using System.Runtime.CompilerServices;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public static class ResourceModelResourceDefinitionBuilderExtensions
{
    private static readonly ConditionalWeakTable<IResourceDefinitionBuilder, ResourceModelDeclarationMetadata>
        DeclarationMetadata = new();

    public static TBuilder WithResourceGroup<TBuilder>(
        this TBuilder builder,
        string? resourceGroupId)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetMetadata(builder).ResourceGroupId = NormalizeOptional(resourceGroupId);
        return builder;
    }

    public static TBuilder WithAutoStart<TBuilder>(
        this TBuilder builder,
        bool autoStart = true)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetMetadata(builder).AutoStart = autoStart;
        return builder;
    }

    public static TBuilder WithDependencyAutoStart<TBuilder>(
        this TBuilder builder,
        bool autoStart = true)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetMetadata(builder).DependencyAutoStart = autoStart;
        return builder;
    }

    public static TBuilder ProvisionIdentityOnStartup<TBuilder>(
        this TBuilder builder,
        bool provision = true)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetMetadata(builder).ProvisionIdentityOnStartup = provision;
        return builder;
    }

    public static TBuilder WithIdentity<TBuilder>(
        this TBuilder builder,
        ResourceIdentityProviderDefinition provider,
        string? subject = null,
        IReadOnlyList<string>? scopes = null,
        IReadOnlyDictionary<string, string>? claims = null,
        string? name = null)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(provider);

        return builder.WithIdentity(
            provider.Id,
            subject,
            scopes,
            claims,
            name);
    }

    public static TBuilder WithIdentity<TBuilder>(
        this TBuilder builder,
        ResourceIdentityProviderDefinition provider,
        Action<ResourceIdentityDeclarationBuilder> configure)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(configure);

        var identity = new ResourceIdentityDeclarationBuilder
        {
            ProviderId = provider.Id
        };
        configure(identity);
        if (string.IsNullOrWhiteSpace(identity.ProviderId))
        {
            identity.ProviderId = provider.Id;
        }

        return builder.WithIdentity(identity.Build());
    }

    public static TBuilder WithIdentity<TBuilder>(
        this TBuilder builder,
        string providerId,
        string? subject = null,
        IReadOnlyList<string>? scopes = null,
        IReadOnlyDictionary<string, string>? claims = null,
        string? name = null)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetMetadata(builder).Identity = new ResourceIdentityBinding(
            providerId,
            subject,
            scopes,
            claims,
            Name: name);
        return builder;
    }

    public static TBuilder WithIdentity<TBuilder>(
        this TBuilder builder,
        ResourceIdentityBinding identity)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(identity);

        GetMetadata(builder).Identity = identity;
        return builder;
    }

    public static TBuilder WithIdentity<TBuilder>(
        this TBuilder builder,
        Action<ResourceIdentityDeclarationBuilder> configure)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var identity = new ResourceIdentityDeclarationBuilder();
        configure(identity);
        return builder.WithIdentity(identity.Build());
    }

    public static TBuilder RequireIdentity<TBuilder>(
        this TBuilder builder,
        IReadOnlyList<string>? scopes = null,
        IReadOnlyDictionary<string, string>? claims = null,
        string? name = null)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithIdentity(
            ResourceIdentityBinding.RequireIdentity(scopes, claims) with
            {
                Name = NormalizeOptional(name)
            });
    }

    public static TBuilder Allow<TBuilder>(
        this TBuilder builder,
        ResourcePrincipalReference principal,
        string permission)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        GetMetadata(builder).PermissionGrants.Add(new ResourcePermissionGrant(
            principal,
            builder.EffectiveResourceId,
            permission));
        return builder;
    }

    public static TBuilder Allow<TBuilder>(
        this TBuilder builder,
        ResourcePrincipalReference principal,
        ResourceAccessPermissionSet access)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(access);

        foreach (var permission in access.Permissions)
        {
            builder.Allow(principal, permission);
        }

        return builder;
    }

    public static TBuilder Allow<TBuilder>(
        this TBuilder builder,
        IResourceDefinitionBuilder resource,
        string permission)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(resource);

        return builder.Allow(resource.Principal(), permission);
    }

    public static TBuilder Allow<TBuilder>(
        this TBuilder builder,
        IResourceDefinitionBuilder resource,
        ResourceAccessPermissionSet access)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(resource);

        return builder.Allow(resource.Principal(), access);
    }

    public static IControlPlaneBuilder AddResourceGroup(
        this IControlPlaneBuilder builder,
        string id,
        string name,
        string description = "")
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetOrAddDeclarationStore(builder.Services)
            .AddResourceGroup(id, name, description);
        return builder;
    }

    public static IControlPlaneBuilder AddIdentityProvider(
        this IControlPlaneBuilder builder,
        string id,
        string name,
        ResourceIdentityProviderKind kind = ResourceIdentityProviderKind.Oidc,
        IReadOnlyDictionary<string, string>? settings = null,
        string? provisioningResourceId = null,
        bool useAsDefault = false)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!string.IsNullOrWhiteSpace(provisioningResourceId))
        {
            GetOrAddDeclarationStore(builder.Services)
                .Declare(
                    builder,
                    ResourceIdentityProvisioningResources.ProviderId,
                    provisioningResourceId.Trim(),
                    resourceClass: CloudShell.Abstractions.ResourceManager.ResourceClass.Infrastructure,
                    attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [ResourceAttributeNames.InfrastructureKind] = "identity-provisioning",
                        ["identity.provider"] = name.Trim()
                    });
        }

        var provider = new ResourceIdentityProviderDefinition(id, name, kind, settings, provisioningResourceId);
        GetOrAddDeclarationStore(builder.Services)
            .AddIdentityProvider(provider, useAsDefault);
        return builder;
    }

    public static IControlPlaneBuilder AddIdentityProvider(
        this IControlPlaneBuilder builder,
        ResourceIdentityProviderDefinition provider,
        bool useAsDefault = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(provider);

        GetOrAddDeclarationStore(builder.Services)
            .AddIdentityProvider(provider, useAsDefault);
        return builder;
    }

    internal static ResourceModelDeclarationMetadata GetDeclarationMetadata(
        IResourceDefinitionBuilder builder) =>
        DeclarationMetadata.TryGetValue(builder, out var metadata)
            ? metadata
            : ResourceModelDeclarationMetadata.Empty;

    internal static ResourceDeclarationStore GetOrAddDeclarationStore(IServiceCollection services)
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

    private static ResourceModelDeclarationMetadata GetMetadata(IResourceDefinitionBuilder builder) =>
        DeclarationMetadata.GetValue(builder, _ => new ResourceModelDeclarationMetadata());

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class ResourceModelDeclarationMetadata
{
    public static ResourceModelDeclarationMetadata Empty { get; } = new();

    public string? ResourceGroupId { get; set; }

    public bool? AutoStart { get; set; }

    public bool? DependencyAutoStart { get; set; }

    public bool? ProvisionIdentityOnStartup { get; set; }

    public ResourceIdentityBinding? Identity { get; set; }

    public List<ResourcePermissionGrant> PermissionGrants { get; } = [];
}
