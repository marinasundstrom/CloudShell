using System.Runtime.CompilerServices;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ControlPlane.ResourceModel;

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

    public static TBuilder WithResourceGroup<TBuilder>(
        this TBuilder builder,
        ResourceGroupDefinition? resourceGroup)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetMetadata(builder).ResourceGroupId = resourceGroup?.Id;
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
        SetProvisionIdentityOnStartupAttribute(builder, provision);
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
        ControlPlaneIdentityProviderContext provider,
        string? subject = null,
        IReadOnlyList<string>? scopes = null,
        IReadOnlyDictionary<string, string>? claims = null,
        string? name = null)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(provider);

        return builder.WithIdentity(
            provider.Provider,
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
        ControlPlaneIdentityProviderContext provider,
        Action<ResourceIdentityDeclarationBuilder> configure)
        where TBuilder : IResourceDefinitionBuilder
    {
        ArgumentNullException.ThrowIfNull(provider);

        return builder.WithIdentity(provider.Provider, configure);
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

        var identity = new ResourceIdentityBinding(
            providerId,
            subject,
            scopes,
            claims,
            Name: name);
        GetMetadata(builder).Identity = identity;
        SetIdentityAttribute(builder, identity);
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
        SetIdentityAttribute(builder, identity);
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
        AddAccessGrantAttribute(builder, principal, permission);
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

    private static void SetIdentityAttribute(
        IResourceDefinitionBuilder builder,
        ResourceIdentityBinding identity)
    {
        if (builder is not IResourceDefinitionAttributeBuilder attributeBuilder)
        {
            return;
        }

        var attribute = new ResourceIdentityBindingAttribute(
            identity.ProviderId,
            identity.Subject,
            identity.IdentityScopes,
            identity.IdentityClaims,
            identity.Kind == ResourceIdentityBindingKind.Required
                ? ResourceIdentityBindingAttributeKinds.Required
                : ResourceIdentityBindingAttributeKinds.Provider,
            identity.Name);

        foreach (var (attributeId, value) in CreateIdentityAttributes(attribute))
        {
            attributeBuilder.SetAttribute(attributeId, value);
        }
    }

    private static void SetProvisionIdentityOnStartupAttribute(
        IResourceDefinitionBuilder builder,
        bool provision)
    {
        if (builder is IResourceDefinitionAttributeBuilder attributeBuilder)
        {
            attributeBuilder.SetAttribute(
                ResourceDeclarationAttributeIds.IdentityProvisionOnStartup,
                ResourceAttributeValue.Boolean(provision));
        }
    }

    private static void AddAccessGrantAttribute(
        IResourceDefinitionBuilder builder,
        ResourcePrincipalReference principal,
        string permission)
    {
        if (builder is not IResourceDefinitionAttributeBuilder attributeBuilder)
        {
            return;
        }

        var grants = GetAccessGrantAttributes(attributeBuilder)
            .Append(new ResourceAccessGrantAttribute(ToAttribute(principal), permission.Trim()))
            .DistinctBy(grant => new AccessGrantAttributeKey(grant))
            .ToArray();
        attributeBuilder.SetAttribute(
            ResourceDeclarationAttributeIds.AccessGrants,
            ResourceAttributeValue.FromObject(grants));
    }

    private static IReadOnlyList<ResourceAccessGrantAttribute> GetAccessGrantAttributes(
        IResourceDefinitionAttributeBuilder builder) =>
        builder.AttributeValues.TryGetValue(ResourceDeclarationAttributeIds.AccessGrants, out var value)
            ? value.ToObject<ResourceAccessGrantAttribute[]>(ResourceDefinitionJson.Options) ?? []
            : [];

    private static Dictionary<ResourceAttributeId, ResourceAttributeValue> CreateIdentityAttributes(
        ResourceIdentityBindingAttribute identity)
    {
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>();
        SetOptional(attributes, ResourceDeclarationAttributeIds.IdentityKind, identity.Kind);
        SetOptional(attributes, ResourceDeclarationAttributeIds.IdentityProviderId, identity.ProviderId);
        SetOptional(attributes, ResourceDeclarationAttributeIds.IdentitySubject, identity.Subject);
        SetOptional(attributes, ResourceDeclarationAttributeIds.IdentityName, identity.Name);

        if (identity.Scopes is { Count: > 0 })
        {
            attributes[ResourceDeclarationAttributeIds.IdentityScopes] =
                ResourceAttributeValue.FromObject(identity.Scopes);
        }

        if (identity.Claims is { Count: > 0 })
        {
            attributes[ResourceDeclarationAttributeIds.IdentityClaims] =
                ResourceAttributeValue.FromObject(identity.Claims);
        }

        return attributes;
    }

    private static ResourcePrincipalReferenceAttribute ToAttribute(ResourcePrincipalReference principal) =>
        new(
            ToAttributeKind(principal.Kind),
            principal.Id,
            principal.DisplayName,
            principal.ProviderId,
            principal.SourceResourceId,
            principal.SourceIdentityName);

    private static string ToAttributeKind(ResourcePrincipalKind kind) =>
        kind switch
        {
            ResourcePrincipalKind.ResourceIdentity => ResourcePrincipalAttributeKinds.ResourceIdentity,
            ResourcePrincipalKind.User => ResourcePrincipalAttributeKinds.User,
            ResourcePrincipalKind.Group => ResourcePrincipalAttributeKinds.Group,
            ResourcePrincipalKind.ServiceAccount => ResourcePrincipalAttributeKinds.ServiceAccount,
            ResourcePrincipalKind.ServicePrincipal => ResourcePrincipalAttributeKinds.ServicePrincipal,
            ResourcePrincipalKind.ManagedIdentity => ResourcePrincipalAttributeKinds.ManagedIdentity,
            ResourcePrincipalKind.WorkloadIdentity => ResourcePrincipalAttributeKinds.WorkloadIdentity,
            ResourcePrincipalKind.External => ResourcePrincipalAttributeKinds.External,
            _ => kind.ToString()
        };

    private static void SetOptional(
        Dictionary<ResourceAttributeId, ResourceAttributeValue> attributes,
        ResourceAttributeId attributeId,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            attributes[attributeId] = ResourceAttributeValue.String(value.Trim());
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record AccessGrantAttributeKey(
        string Kind,
        string Id,
        string? ProviderId,
        string Permission)
    {
        public AccessGrantAttributeKey(ResourceAccessGrantAttribute grant)
            : this(
                grant.Principal.Kind,
                grant.Principal.Id,
                grant.Principal.ProviderId,
                grant.Permission)
        {
        }
    }
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
