using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ResourceManagerClass = CloudShell.Abstractions.ResourceManager.ResourceClass;

namespace CloudShell.ControlPlane.ResourceModel;

public sealed class ControlPlaneResourceDefinitionGraphBuilder :
    ResourceDefinitionGraphBuilder
{
    private readonly ControlPlaneResourceDefinitionContextMetadata _metadata = new();
    private readonly IControlPlaneBuilder? _builder;
    private readonly ResourceDeclarationStore? _declarations;
    private readonly IResourceDeclarationBuilder? _declarationBuilder;

    public ControlPlaneResourceDefinitionGraphBuilder(IResourceIdConvention? resourceIdConvention = null)
        : base(resourceIdConvention)
    {
    }

    internal ControlPlaneResourceDefinitionGraphBuilder(
        IControlPlaneBuilder builder,
        ResourceDeclarationStore declarations,
        IResourceIdConvention? resourceIdConvention = null)
        : base(resourceIdConvention)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(declarations);

        _builder = builder;
        _declarations = declarations;
        _declarationBuilder = new ControlPlaneResourceDeclarationBuilder(builder, declarations);
    }

    internal ControlPlaneResourceDefinitionContextMetadata ContextMetadata => _metadata;

    public IResourceDeclarationBuilder Declarations => _declarationBuilder
        ?? throw new InvalidOperationException(
            "Provider-backed declarations are only available from Control Plane host authoring callbacks.");

    public IServiceCollection Services => _builder?.Services
        ?? throw new InvalidOperationException(
            "Control Plane services are only available from Control Plane host authoring callbacks.");

    public IResourceBuilder Declare(
        string providerId,
        string resourceId,
        string? parentResourceId = null,
        string? resourceGroupId = null,
        IReadOnlyList<string>? dependsOn = null,
        ResourceDeclarationPersistence persistence = ResourceDeclarationPersistence.Transient,
        bool overwritePersistedState = false,
        ResourceManagerClass? resourceClass = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        Action<ResourceDeclaration>? onChanged = null,
        ResourceIdentityBinding? identity = null)
    {
        var builder = _builder ?? throw new InvalidOperationException(
            "Provider-backed declarations are only available from Control Plane host authoring callbacks.");
        var declarations = _declarations ?? throw new InvalidOperationException(
            "Provider-backed declarations are only available from Control Plane host authoring callbacks.");

        return declarations.Declare(
            builder,
            providerId,
            resourceId,
            parentResourceId,
            resourceGroupId,
            dependsOn,
            persistence,
            overwritePersistedState,
            resourceClass,
            attributes,
            onChanged,
            identity);
    }

    public ControlPlaneResourceDefinitionGraphBuilder UseDefaultIdentityProvider(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        _metadata.UseDefaultIdentityProvider(providerId);
        return this;
    }

    public ControlPlaneIdentityProviderContext GetIdentityProvider(string? providerId = null) =>
        new(_metadata.GetIdentityProvider(providerId));

    public ControlPlaneIdentityProviderContext AddIdentityProvider(
        string id,
        string name,
        ResourceIdentityProviderKind kind = ResourceIdentityProviderKind.Oidc,
        IReadOnlyDictionary<string, string>? settings = null,
        string? provisioningResourceId = null,
        bool useAsDefault = false) =>
        AddIdentityProvider(
            new ResourceIdentityProviderDefinition(id, name, kind, settings, provisioningResourceId),
            useAsDefault);

    public ControlPlaneIdentityProviderContext AddIdentityProvider(
        ResourceIdentityProviderDefinition provider,
        bool useAsDefault = false)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return new ControlPlaneIdentityProviderContext(
            _metadata.AddIdentityProvider(provider, useAsDefault));
    }

    internal void SeedIdentityProviderDeclarations(ResourceDeclarationStore declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        var defaultProviderId = declarations.DefaultIdentityProviderId;
        foreach (var provider in declarations.GetIdentityProviders())
        {
            _metadata.AddIdentityProvider(
                provider,
                string.Equals(provider.Id, defaultProviderId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(defaultProviderId))
        {
            _metadata.UseDefaultIdentityProvider(defaultProviderId);
        }
    }
}

internal sealed class ControlPlaneResourceDeclarationBuilder(
    IControlPlaneBuilder builder,
    ResourceDeclarationStore declarations) : IResourceDeclarationBuilder
{
    public ICloudShellBuilder CloudShellBuilder { get; } = builder;

    public IServiceCollection Services => CloudShellBuilder.Services;

    public IResourceBuilder Declare(
        string providerId,
        string resourceId,
        string? parentResourceId = null,
        string? resourceGroupId = null,
        IReadOnlyList<string>? dependsOn = null,
        ResourceDeclarationPersistence persistence = ResourceDeclarationPersistence.Transient,
        bool overwritePersistedState = false,
        ResourceManagerClass? resourceClass = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        Action<ResourceDeclaration>? onChanged = null,
        ResourceIdentityBinding? identity = null) =>
        declarations.Declare(
            CloudShellBuilder,
            providerId,
            resourceId,
            parentResourceId,
            resourceGroupId,
            dependsOn,
            persistence,
            overwritePersistedState,
            resourceClass,
            attributes,
            onChanged,
            identity);
}

internal sealed class ControlPlaneResourceDefinitionContextMetadata
{
    public static ControlPlaneResourceDefinitionContextMetadata Empty { get; } = new();

    private readonly Dictionary<string, ResourceIdentityProviderDefinition> _identityProviders =
        new(StringComparer.OrdinalIgnoreCase);

    public string? DefaultIdentityProviderId { get; private set; }

    public ResourceIdentityProviderDefinition AddIdentityProvider(
        ResourceIdentityProviderDefinition provider,
        bool useAsDefault = false)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var normalized = new ResourceIdentityProviderCatalog([provider]).Providers.Single();
        _identityProviders[normalized.Id] = normalized;
        if (useAsDefault)
        {
            DefaultIdentityProviderId = normalized.Id;
        }

        return normalized;
    }

    public void UseDefaultIdentityProvider(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        DefaultIdentityProviderId = providerId.Trim();
    }

    public IReadOnlyList<ResourceIdentityProviderDefinition> GetIdentityProviders() =>
        _identityProviders.Values
            .OrderBy(provider => provider.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public ResourceIdentityProviderDefinition GetIdentityProvider(string? providerId = null)
    {
        if (!string.IsNullOrWhiteSpace(providerId))
        {
            var normalizedProviderId = providerId.Trim();
            return _identityProviders.TryGetValue(normalizedProviderId, out var provider)
                ? provider
                : throw new InvalidOperationException(
                    $"Identity provider '{normalizedProviderId}' is not registered in the Control Plane resource definition context.");
        }

        if (!string.IsNullOrWhiteSpace(DefaultIdentityProviderId))
        {
            if (_identityProviders.TryGetValue(DefaultIdentityProviderId, out var provider))
            {
                return provider;
            }

            throw new InvalidOperationException(
                $"Default identity provider '{DefaultIdentityProviderId}' is not registered in the Control Plane resource definition context.");
        }

        return _identityProviders.Count == 1
            ? _identityProviders.Values.Single()
            : throw new InvalidOperationException(
                "No default identity provider is registered in the Control Plane resource definition context.");
    }
}
