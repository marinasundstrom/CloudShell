using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.ResourceManager;

public enum ResourceDeclarationPersistence
{
    Transient,
    Persisted
}

public sealed record ResourceDeclaration(
    string ProviderId,
    string ResourceId,
    string? ParentResourceId,
    string? ResourceGroupId,
    DateTimeOffset DeclaredAt,
    IReadOnlyList<string> DependsOn,
    ResourceDeclarationPersistence Persistence,
    bool OverwritePersistedState = false,
    bool? AutoStartOverride = null,
    bool? DependencyAutoStartOverride = null,
    bool ProvisionIdentityOnStartup = false,
    ResourceClass? ResourceClassOverride = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    ResourceIdentityBinding? Identity = null)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> ResourceAttributes => Attributes ?? EmptyAttributes;

    public ResourceIdentityBinding? IdentityBinding => Identity;
}

public interface IResourceBuilder
{
    ICloudShellBuilder CloudShellBuilder { get; }

    string ResourceId { get; }

    ResourceIdentityReference Identity { get; }

    IResourceBuilder WithResourceGroup(string? resourceGroupId);

    IResourceBuilder WithParent(string? parentResourceId);

    IResourceBuilder WithParent(IResourceBuilder resource);

    IResourceBuilder DependsOn(string resourceId);

    IResourceBuilder DependsOn(IResourceBuilder resource);

    IResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    IResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources);

    IResourceBuilder WithReference(string resourceId);

    IResourceBuilder WithReference(IResourceBuilder resource);

    IResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    IResourceBuilder Persist(bool overwrite = false);
}

public interface IResourceDeclarationBuilder
{
    ICloudShellBuilder CloudShellBuilder { get; }

    IServiceCollection Services { get; }

    IResourceBuilder Declare(
        string providerId,
        string resourceId,
        string? parentResourceId = null,
        string? resourceGroupId = null,
        IReadOnlyList<string>? dependsOn = null,
        ResourceDeclarationPersistence persistence = ResourceDeclarationPersistence.Transient,
        bool overwritePersistedState = false,
        ResourceClass? resourceClass = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        Action<ResourceDeclaration>? onChanged = null,
        ResourceIdentityBinding? identity = null);
}

public interface IResourceGraphBuilder : IResourceDeclarationBuilder
{
}

public interface IProgrammaticResourceDeclarationProvider
{
    bool CanApplyDeclaration(ResourceDeclaration declaration);

    Task ApplyDeclarationAsync(
        ResourceDeclaration declaration,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default);
}

public sealed record ResourcePermissionGrant(
    ResourceIdentityReference Identity,
    string TargetResourceId,
    string Permission)
{
    public string TargetResourceId { get; init; } = RequireValue(TargetResourceId);

    public string Permission { get; init; } = RequireValue(Permission);

    private static string RequireValue(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }
}

public sealed record ResourcePermissionEvaluation(
    ResourceIdentityReference Identity,
    string TargetResourceId,
    string Permission,
    bool IsAllowed,
    ResourcePermissionGrant? Grant = null);

public sealed class ResourcePermissionGrantEvaluator(
    IEnumerable<ResourcePermissionGrant> grants)
{
    private readonly IReadOnlyList<ResourcePermissionGrant> grants = grants.ToArray();

    public ResourcePermissionEvaluation Evaluate(
        ResourceIdentityReference identity,
        string targetResourceId,
        string permission)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        var normalizedTargetResourceId = targetResourceId.Trim();
        var normalizedPermission = permission.Trim();
        var grant = grants.FirstOrDefault(grant =>
            MatchesIdentity(grant.Identity, identity) &&
            Matches(grant.TargetResourceId, normalizedTargetResourceId) &&
            MatchesPermission(grant.Permission, normalizedPermission));

        return new ResourcePermissionEvaluation(
            identity,
            normalizedTargetResourceId,
            normalizedPermission,
            grant is not null,
            grant);
    }

    private static bool MatchesIdentity(
        ResourceIdentityReference grantIdentity,
        ResourceIdentityReference requestedIdentity) =>
        Matches(grantIdentity.ResourceId, requestedIdentity.ResourceId) &&
        (string.IsNullOrWhiteSpace(grantIdentity.Name) ||
         Matches(grantIdentity.Name, requestedIdentity.Name));

    private static bool MatchesPermission(string grantPermission, string requestedPermission) =>
        Matches(grantPermission, requestedPermission) ||
        Matches(grantPermission, CloudShellPermissions.All);

    private static bool Matches(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

public static class ResourceDeclarationBuilderExtensions
{
    public static IResourceGraphBuilder UseDefaultIdentityProvider(
        this IResourceGraphBuilder builder,
        string providerId)
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetOrAddDeclarationStore(builder.Services)
            .UseDefaultIdentityProvider(providerId);
        return builder;
    }

    public static ResourceIdentityProviderDefinition AddIdentityProvider(
        this IResourceGraphBuilder builder,
        string id,
        string name,
        ResourceIdentityProviderKind kind = ResourceIdentityProviderKind.Oidc,
        IReadOnlyDictionary<string, string>? settings = null,
        string? provisioningResourceId = null,
        bool useAsDefault = false)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var provider = new ResourceIdentityProviderDefinition(id, name, kind, settings, provisioningResourceId);
        return GetOrAddDeclarationStore(builder.Services)
            .AddIdentityProvider(provider, useAsDefault);
    }

    public static IControlPlaneBuilder Resources(
        this IControlPlaneBuilder builder,
        Action<IResourceGraphBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var declarations = GetOrAddDeclarationStore(builder.Services);
        configure(new ResourceGraphBuilder(builder, declarations));
        return builder;
    }

    public static IControlPlaneBuilder ConfigureResources(
        this IControlPlaneBuilder builder,
        Action<IResourceDeclarationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var declarations = GetOrAddDeclarationStore(builder.Services);

        configure(new ResourceGraphBuilder(builder, declarations));
        return builder;
    }

    public static IControlPlaneBuilder AddResources(
        this IControlPlaneBuilder builder,
        Action<IResourceDeclarationBuilder> configure) =>
        builder.ConfigureResources(configure);

    public static IResourceDeclarationBuilder WithAutoStart(
        this IResourceDeclarationBuilder builder,
        bool autoStart = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetOrAddDeclarationStore(builder.Services).SetDefaultAutoStart(autoStart);
        return builder;
    }

    public static IResourceGraphBuilder WithAutoStart(
        this IResourceGraphBuilder builder,
        bool autoStart = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetOrAddDeclarationStore(builder.Services).SetDefaultAutoStart(autoStart);
        return builder;
    }

    public static TBuilder WithAutoStart<TBuilder>(
        this TBuilder builder,
        bool autoStart = true)
        where TBuilder : IResourceBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetOrAddDeclarationStore(builder.CloudShellBuilder.Services)
            .SetAutoStart(builder.ResourceId, autoStart);
        return builder;
    }

    public static IResourceDeclarationBuilder WithDependencyAutoStart(
        this IResourceDeclarationBuilder builder,
        bool autoStart = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetOrAddDeclarationStore(builder.Services).SetDefaultDependencyAutoStart(autoStart);
        return builder;
    }

    public static IResourceGraphBuilder WithDependencyAutoStart(
        this IResourceGraphBuilder builder,
        bool autoStart = true)
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetOrAddDeclarationStore(builder.Services).SetDefaultDependencyAutoStart(autoStart);
        return builder;
    }

    public static TBuilder WithDependencyAutoStart<TBuilder>(
        this TBuilder builder,
        bool autoStart = true)
        where TBuilder : IResourceBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetOrAddDeclarationStore(builder.CloudShellBuilder.Services)
            .SetDependencyAutoStart(builder.ResourceId, autoStart);
        return builder;
    }

    public static TBuilder ProvisionIdentityOnStartup<TBuilder>(
        this TBuilder builder,
        bool provision = true)
        where TBuilder : IResourceBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetOrAddDeclarationStore(builder.CloudShellBuilder.Services)
            .SetProvisionIdentityOnStartup(builder.ResourceId, provision);
        return builder;
    }

    public static TBuilder WithResourceClass<TBuilder>(
        this TBuilder builder,
        ResourceClass resourceClass)
        where TBuilder : IResourceBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetOrAddDeclarationStore(builder.CloudShellBuilder.Services)
            .SetResourceClass(builder.ResourceId, resourceClass);
        return builder;
    }

    public static TBuilder WithResourceAttribute<TBuilder>(
        this TBuilder builder,
        string name,
        string value)
        where TBuilder : IResourceBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetOrAddDeclarationStore(builder.CloudShellBuilder.Services)
            .SetAttribute(builder.ResourceId, name, value);
        return builder;
    }

    public static TBuilder WithResourceAttributes<TBuilder>(
        this TBuilder builder,
        IReadOnlyDictionary<string, string> attributes)
        where TBuilder : IResourceBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(attributes);

        GetOrAddDeclarationStore(builder.CloudShellBuilder.Services)
            .SetAttributes(builder.ResourceId, attributes);
        return builder;
    }

    public static TBuilder WithIdentity<TBuilder>(
        this TBuilder builder,
        ResourceIdentityProviderDefinition provider,
        string? subject = null,
        IReadOnlyList<string>? scopes = null,
        IReadOnlyDictionary<string, string>? claims = null,
        string? name = null)
        where TBuilder : IResourceBuilder
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
        where TBuilder : IResourceBuilder
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
        where TBuilder : IResourceBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        GetOrAddDeclarationStore(builder.CloudShellBuilder.Services)
            .SetIdentity(
                builder.ResourceId,
                new ResourceIdentityBinding(
                    providerId,
                    subject,
                    scopes,
                    claims,
                    Name: name));
        return builder;
    }

    public static TBuilder WithIdentity<TBuilder>(
        this TBuilder builder,
        ResourceIdentityBinding identity)
        where TBuilder : IResourceBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(identity);

        GetOrAddDeclarationStore(builder.CloudShellBuilder.Services)
            .SetIdentity(builder.ResourceId, identity);
        return builder;
    }

    public static TBuilder WithIdentity<TBuilder>(
        this TBuilder builder,
        Action<ResourceIdentityDeclarationBuilder> configure)
        where TBuilder : IResourceBuilder
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
        where TBuilder : IResourceBuilder
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
        ResourceIdentityReference identity,
        string permission)
        where TBuilder : IResourceBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(identity);

        GetOrAddDeclarationStore(builder.CloudShellBuilder.Services)
            .AddPermissionGrant(new ResourcePermissionGrant(
                identity,
                builder.ResourceId,
                permission));
        return builder;
    }

    public static TBuilder Allow<TBuilder>(
        this TBuilder builder,
        IResourceBuilder resource,
        string permission)
        where TBuilder : IResourceBuilder
    {
        ArgumentNullException.ThrowIfNull(resource);
        return builder.Allow(resource.Identity, permission);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

internal sealed class ResourceGraphBuilder(
    ICloudShellBuilder cloudShellBuilder,
    ResourceDeclarationStore declarations) : IResourceGraphBuilder
{
    public ICloudShellBuilder CloudShellBuilder { get; } = cloudShellBuilder;

    public IServiceCollection Services => CloudShellBuilder.Services;

    public IResourceBuilder Declare(
        string providerId,
        string resourceId,
        string? parentResourceId = null,
        string? resourceGroupId = null,
        IReadOnlyList<string>? dependsOn = null,
        ResourceDeclarationPersistence persistence = ResourceDeclarationPersistence.Transient,
        bool overwritePersistedState = false,
        ResourceClass? resourceClass = null,
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

public sealed class ResourceIdentityDeclarationBuilder
{
    public string? Name { get; set; }

    public string? Provider
    {
        get => ProviderId;
        set => ProviderId = value;
    }

    public string? ProviderId { get; set; }

    public string? Subject { get; set; }

    public List<string> Scopes { get; } = [];

    public Dictionary<string, string> Claims { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public ResourceIdentityBinding Build()
    {
        var providerId = NormalizeOptional(ProviderId);
        var name = NormalizeOptional(Name);
        var subject = NormalizeOptional(Subject);
        var scopes = Scopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var claims = Claims
            .Where(claim => !string.IsNullOrWhiteSpace(claim.Key) &&
                            !string.IsNullOrWhiteSpace(claim.Value))
            .ToDictionary(
                claim => claim.Key.Trim(),
                claim => claim.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);

        return providerId is null
            ? ResourceIdentityBinding.RequireIdentity(scopes, claims) with { Name = name, Subject = subject }
            : new ResourceIdentityBinding(
                providerId,
                subject,
                scopes,
                claims,
                Name: name);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ResourceDeclarationStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ResourceDeclaration> _declarations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Action<ResourceDeclaration>>> _changeHandlers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ResourceIdentityProviderDefinition> _identityProviders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ResourcePermissionGrant> _permissionGrants = [];
    private string? _defaultIdentityProviderId;
    private bool _defaultAutoStart = true;
    private bool _defaultDependencyAutoStart = true;

    public bool DefaultAutoStart
    {
        get
        {
            lock (_gate)
            {
                return _defaultAutoStart;
            }
        }
    }

    public bool DefaultDependencyAutoStart
    {
        get
        {
            lock (_gate)
            {
                return _defaultDependencyAutoStart;
            }
        }
    }

    public string? DefaultIdentityProviderId
    {
        get
        {
            lock (_gate)
            {
                return _defaultIdentityProviderId;
            }
        }
    }

    public ResourceIdentityProviderDefinition AddIdentityProvider(
        ResourceIdentityProviderDefinition provider,
        bool useAsDefault = false)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var normalized = new ResourceIdentityProviderCatalog([provider]).Providers.Single();
        lock (_gate)
        {
            _identityProviders[normalized.Id] = normalized;
            if (useAsDefault)
            {
                _defaultIdentityProviderId = normalized.Id;
            }
        }

        return normalized;
    }

    public void UseDefaultIdentityProvider(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        lock (_gate)
        {
            _defaultIdentityProviderId = providerId.Trim();
        }
    }

    public IReadOnlyList<ResourceIdentityProviderDefinition> GetIdentityProviders()
    {
        lock (_gate)
        {
            return _identityProviders.Values
                .OrderBy(provider => provider.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public ResourceIdentityProviderCatalog CreateIdentityProviderCatalog(
        ResourceIdentityProviderCatalog configuredProviders)
    {
        ArgumentNullException.ThrowIfNull(configuredProviders);
        lock (_gate)
        {
            return configuredProviders.Merge(_identityProviders.Values, _defaultIdentityProviderId);
        }
    }

    public IResourceBuilder Declare(
        ICloudShellBuilder builder,
        string providerId,
        string resourceId,
        string? parentResourceId = null,
        string? resourceGroupId = null,
        IReadOnlyList<string>? dependsOn = null,
        ResourceDeclarationPersistence persistence = ResourceDeclarationPersistence.Transient,
        bool overwritePersistedState = false,
        ResourceClass? resourceClass = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        Action<ResourceDeclaration>? onChanged = null,
        ResourceIdentityBinding? identity = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        ResourceDeclaration declaration;
        lock (_gate)
        {
            var existing = _declarations.GetValueOrDefault(resourceId.Trim());
            var normalized = Normalize(
                providerId,
                resourceId,
                parentResourceId,
                resourceGroupId,
                dependsOn ?? [],
                persistence,
                overwritePersistedState,
                existing?.AutoStartOverride,
                existing?.DependencyAutoStartOverride,
                existing?.ProvisionIdentityOnStartup ?? false,
                resourceClass ?? existing?.ResourceClassOverride,
                attributes is null
                    ? existing?.ResourceAttributes
                    : MergeAttributes(existing?.ResourceAttributes, attributes),
                identity ?? existing?.IdentityBinding);

            declaration = existing is not null
                ? normalized with { DeclaredAt = existing.DeclaredAt }
                : normalized;

            _declarations[declaration.ResourceId] = declaration;

            if (onChanged is not null)
            {
                if (!_changeHandlers.TryGetValue(declaration.ResourceId, out var handlers))
                {
                    handlers = [];
                    _changeHandlers[declaration.ResourceId] = handlers;
                }

                handlers.Add(onChanged);
            }
        }

        NotifyChanged(declaration);
        return new ResourceBuilder(builder, this, declaration.ResourceId);
    }

    public IReadOnlyList<ResourceDeclaration> GetDeclarations()
    {
        lock (_gate)
        {
            return _declarations.Values
                .OrderBy(declaration => declaration.DeclaredAt)
                .ToArray();
        }
    }

    public ResourceDeclaration? GetDeclaration(string resourceId)
    {
        lock (_gate)
        {
            return _declarations.GetValueOrDefault(resourceId);
        }
    }

    public IReadOnlyList<ResourcePermissionGrant> GetPermissionGrants()
    {
        lock (_gate)
        {
            return _permissionGrants
                .OrderBy(grant => grant.TargetResourceId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(grant => grant.Identity.ResourceId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(grant => grant.Identity.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(grant => grant.Permission, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public ResourcePermissionGrantEvaluator CreatePermissionGrantEvaluator() =>
        new(GetPermissionGrants());

    public void Remove(string resourceId)
    {
        lock (_gate)
        {
            _declarations.Remove(resourceId);
        }
    }

    public void AssignToGroup(string resourceId, string? resourceGroupId)
    {
        Update(resourceId, declaration => declaration with
        {
            ResourceGroupId = NormalizeGroupId(resourceGroupId)
        });
    }

    public void AssignParent(string resourceId, string? parentResourceId)
    {
        Update(resourceId, declaration => declaration with
        {
            ParentResourceId = NormalizeResourceId(parentResourceId)
        });
    }

    public void SetDependencies(string resourceId, IReadOnlyList<string> dependsOn)
    {
        Update(resourceId, declaration => declaration with
        {
            DependsOn = NormalizeDependencies(dependsOn)
        });
    }

    public void Persist(string resourceId, bool overwrite = false)
    {
        Update(resourceId, declaration => declaration with
        {
            Persistence = ResourceDeclarationPersistence.Persisted,
            OverwritePersistedState = overwrite
        });
    }

    public void SetDefaultAutoStart(bool autoStart)
    {
        lock (_gate)
        {
            _defaultAutoStart = autoStart;
        }
    }

    public void SetDefaultDependencyAutoStart(bool autoStart)
    {
        lock (_gate)
        {
            _defaultDependencyAutoStart = autoStart;
        }
    }

    public void SetAutoStart(string resourceId, bool autoStart)
    {
        Update(resourceId, declaration => declaration with
        {
            AutoStartOverride = autoStart
        });
    }

    public void SetDependencyAutoStart(string resourceId, bool autoStart)
    {
        Update(resourceId, declaration => declaration with
        {
            DependencyAutoStartOverride = autoStart
        });
    }

    public void SetProvisionIdentityOnStartup(string resourceId, bool provision)
    {
        Update(resourceId, declaration => declaration with
        {
            ProvisionIdentityOnStartup = provision
        });
    }

    public void SetResourceClass(string resourceId, ResourceClass? resourceClass)
    {
        Update(resourceId, declaration => declaration with
        {
            ResourceClassOverride = resourceClass
        });
    }

    public void SetAttributes(
        string resourceId,
        IReadOnlyDictionary<string, string> attributes)
    {
        Update(resourceId, declaration => declaration with
        {
            Attributes = MergeAttributes(declaration.ResourceAttributes, attributes)
        });
    }

    public void SetAttribute(string resourceId, string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Update(resourceId, declaration => declaration with
        {
            Attributes = MergeAttributes(
                declaration.ResourceAttributes,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [name] = value
                })
        });
    }

    public void SetIdentity(string resourceId, ResourceIdentityBinding? identity)
    {
        Update(resourceId, declaration => declaration with
        {
            Identity = identity
        });
    }

    public void AddPermissionGrant(ResourcePermissionGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        lock (_gate)
        {
            if (_permissionGrants.Any(existing =>
                    string.Equals(existing.TargetResourceId, grant.TargetResourceId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.Permission, grant.Permission, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.Identity.ResourceId, grant.Identity.ResourceId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.Identity.Name, grant.Identity.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            _permissionGrants.Add(grant);
        }
    }

    public bool ShouldAutoStart(string resourceId)
    {
        lock (_gate)
        {
            return _declarations.GetValueOrDefault(resourceId)?.AutoStartOverride ??
                _defaultAutoStart;
        }
    }

    public bool ShouldAutoStartAsDependency(string resourceId)
    {
        lock (_gate)
        {
            return _declarations.GetValueOrDefault(resourceId)?.DependencyAutoStartOverride ??
                _defaultDependencyAutoStart;
        }
    }

    private void Update(
        string resourceId,
        Func<ResourceDeclaration, ResourceDeclaration> update)
    {
        ResourceDeclaration updated;
        lock (_gate)
        {
            var current = _declarations.GetValueOrDefault(resourceId)
                ?? throw new InvalidOperationException($"Resource '{resourceId}' is not declared.");
            updated = update(current);
            _declarations[updated.ResourceId] = updated;
        }

        NotifyChanged(updated);
    }

    private void NotifyChanged(ResourceDeclaration declaration)
    {
        IReadOnlyList<Action<ResourceDeclaration>> handlers;
        lock (_gate)
        {
            handlers = _changeHandlers.TryGetValue(declaration.ResourceId, out var registeredHandlers)
                ? registeredHandlers.ToArray()
                : [];
        }

        foreach (var handler in handlers)
        {
            handler(declaration);
        }
    }

    private static ResourceDeclaration Normalize(
        string providerId,
        string resourceId,
        string? parentResourceId,
        string? resourceGroupId,
        IReadOnlyList<string> dependsOn,
        ResourceDeclarationPersistence persistence,
        bool overwritePersistedState,
        bool? autoStartOverride = null,
        bool? dependencyAutoStartOverride = null,
        bool provisionIdentityOnStartup = false,
        ResourceClass? resourceClass = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        ResourceIdentityBinding? identity = null) =>
        new(
            providerId.Trim(),
            resourceId.Trim(),
            NormalizeResourceId(parentResourceId),
            NormalizeGroupId(resourceGroupId),
            DateTimeOffset.UtcNow,
            NormalizeDependencies(dependsOn),
            persistence,
            overwritePersistedState,
            autoStartOverride,
            dependencyAutoStartOverride,
            provisionIdentityOnStartup,
            resourceClass,
            NormalizeAttributes(attributes),
            identity);

    private static string? NormalizeGroupId(string? resourceGroupId) =>
        string.IsNullOrWhiteSpace(resourceGroupId) ? null : resourceGroupId.Trim();

    private static string? NormalizeResourceId(string? resourceId) =>
        string.IsNullOrWhiteSpace(resourceId) ? null : resourceId.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> NormalizeDependencies(IReadOnlyList<string> dependsOn) =>
        dependsOn
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
            .Select(dependency => dependency.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyDictionary<string, string>? NormalizeAttributes(
        IReadOnlyDictionary<string, string>? attributes)
    {
        if (attributes is null || attributes.Count == 0)
        {
            return null;
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in attributes)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            normalized[name.Trim()] = value.Trim();
        }

        return normalized.Count == 0 ? null : normalized;
    }

    private static IReadOnlyDictionary<string, string>? MergeAttributes(
        IReadOnlyDictionary<string, string>? existing,
        IReadOnlyDictionary<string, string> attributes)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (existing is not null)
        {
            foreach (var (name, value) in existing)
            {
                merged[name] = value;
            }
        }

        foreach (var (name, value) in attributes)
        {
            merged[name] = value;
        }

        return NormalizeAttributes(merged);
    }
}

internal sealed class ResourceBuilder(
    ICloudShellBuilder cloudShellBuilder,
    ResourceDeclarationStore declarations,
    string resourceId) : IResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder { get; } = cloudShellBuilder;

    public string ResourceId { get; } = resourceId;

    public ResourceIdentityReference Identity =>
        ResourceIdentityReference.ForResource(
            ResourceId,
            declarations.GetDeclaration(ResourceId)?.IdentityBinding?.Name);

    public IResourceBuilder WithResourceGroup(string? resourceGroupId)
    {
        declarations.AssignToGroup(ResourceId, resourceGroupId);
        return this;
    }

    public IResourceBuilder WithParent(string? parentResourceId)
    {
        declarations.AssignParent(ResourceId, parentResourceId);
        return this;
    }

    public IResourceBuilder WithParent(IResourceBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return WithParent(resource.ResourceId);
    }

    public IResourceBuilder DependsOn(string resourceId)
    {
        var declaration = declarations.GetDeclaration(ResourceId)
            ?? throw new InvalidOperationException($"Resource '{ResourceId}' is not declared.");
        declarations.SetDependencies(
            ResourceId,
            declaration.DependsOn.Append(resourceId).ToArray());
        return this;
    }

    public IResourceBuilder DependsOn(IResourceBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return DependsOn(resource.ResourceId);
    }

    public IResourceBuilder DependsOn(IEnumerable<string> resourceIds)
    {
        var declaration = declarations.GetDeclaration(ResourceId)
            ?? throw new InvalidOperationException($"Resource '{ResourceId}' is not declared.");
        declarations.SetDependencies(
            ResourceId,
            declaration.DependsOn.Concat(resourceIds).ToArray());
        return this;
    }

    public IResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return DependsOn(resources.Select(resource =>
        {
            ArgumentNullException.ThrowIfNull(resource);
            return resource.ResourceId;
        }));
    }

    public IResourceBuilder WithReference(string resourceId) =>
        DependsOn(resourceId);

    public IResourceBuilder WithReference(IResourceBuilder resource) =>
        DependsOn(resource);

    public IResourceBuilder WithReferences(IEnumerable<string> resourceIds) =>
        DependsOn(resourceIds);

    public IResourceBuilder Persist(bool overwrite = false)
    {
        declarations.Persist(ResourceId, overwrite);
        return this;
    }
}
