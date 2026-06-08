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
    ResourceClass? ResourceClassOverride = null,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> ResourceAttributes => Attributes ?? EmptyAttributes;
}

public interface IResourceBuilder
{
    ICloudShellBuilder CloudShellBuilder { get; }

    string ResourceId { get; }

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
        Action<ResourceDeclaration>? onChanged = null);
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

public static class ResourceDeclarationBuilderExtensions
{
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
        Action<ResourceDeclaration>? onChanged = null) =>
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
            onChanged);
}

public sealed class ResourceDeclarationStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ResourceDeclaration> _declarations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Action<ResourceDeclaration>>> _changeHandlers =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _defaultAutoStart = true;

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
        Action<ResourceDeclaration>? onChanged = null)
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
                resourceClass ?? existing?.ResourceClassOverride,
                attributes is null
                    ? existing?.ResourceAttributes
                    : MergeAttributes(existing?.ResourceAttributes, attributes));

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

    public void SetAutoStart(string resourceId, bool autoStart)
    {
        Update(resourceId, declaration => declaration with
        {
            AutoStartOverride = autoStart
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

    public bool ShouldAutoStart(string resourceId)
    {
        lock (_gate)
        {
            return _declarations.GetValueOrDefault(resourceId)?.AutoStartOverride ??
                _defaultAutoStart;
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
        ResourceClass? resourceClass = null,
        IReadOnlyDictionary<string, string>? attributes = null) =>
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
            resourceClass,
            NormalizeAttributes(attributes));

    private static string? NormalizeGroupId(string? resourceGroupId) =>
        string.IsNullOrWhiteSpace(resourceGroupId) ? null : resourceGroupId.Trim();

    private static string? NormalizeResourceId(string? resourceId) =>
        string.IsNullOrWhiteSpace(resourceId) ? null : resourceId.Trim();

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
