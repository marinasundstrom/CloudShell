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
    bool OverwritePersistedState = false);

public interface ICloudShellResourceBuilder
{
    ICloudShellBuilder CloudShellBuilder { get; }

    string ResourceId { get; }

    ICloudShellResourceBuilder WithResourceGroup(string? resourceGroupId);

    ICloudShellResourceBuilder WithParent(string? parentResourceId);

    ICloudShellResourceBuilder WithParent(ICloudShellResourceBuilder resource);

    ICloudShellResourceBuilder DependsOn(string resourceId);

    ICloudShellResourceBuilder DependsOn(ICloudShellResourceBuilder resource);

    ICloudShellResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    ICloudShellResourceBuilder DependsOn(IEnumerable<ICloudShellResourceBuilder> resources);

    ICloudShellResourceBuilder WithReference(string resourceId);

    ICloudShellResourceBuilder WithReference(ICloudShellResourceBuilder resource);

    ICloudShellResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    ICloudShellResourceBuilder Persist(bool overwrite = false);
}

public interface ICloudShellResourceDeclarationBuilder
{
    ICloudShellBuilder CloudShellBuilder { get; }

    IServiceCollection Services { get; }

    ICloudShellResourceBuilder Declare(
        string providerId,
        string resourceId,
        string? parentResourceId = null,
        string? resourceGroupId = null,
        IReadOnlyList<string>? dependsOn = null,
        ResourceDeclarationPersistence persistence = ResourceDeclarationPersistence.Transient,
        bool overwritePersistedState = false,
        Action<ResourceDeclaration>? onChanged = null);
}

public interface IResourceGraphBuilder : ICloudShellResourceDeclarationBuilder
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

public static class CloudShellResourceDeclarationBuilderExtensions
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
        Action<ICloudShellResourceDeclarationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var declarations = GetOrAddDeclarationStore(builder.Services);

        configure(new ResourceGraphBuilder(builder, declarations));
        return builder;
    }

    public static IControlPlaneBuilder AddResources(
        this IControlPlaneBuilder builder,
        Action<ICloudShellResourceDeclarationBuilder> configure) =>
        builder.ConfigureResources(configure);

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

    public ICloudShellResourceBuilder Declare(
        string providerId,
        string resourceId,
        string? parentResourceId = null,
        string? resourceGroupId = null,
        IReadOnlyList<string>? dependsOn = null,
        ResourceDeclarationPersistence persistence = ResourceDeclarationPersistence.Transient,
        bool overwritePersistedState = false,
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
            onChanged);
}

public sealed class ResourceDeclarationStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ResourceDeclaration> _declarations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Action<ResourceDeclaration>>> _changeHandlers =
        new(StringComparer.OrdinalIgnoreCase);

    public ICloudShellResourceBuilder Declare(
        ICloudShellBuilder builder,
        string providerId,
        string resourceId,
        string? parentResourceId = null,
        string? resourceGroupId = null,
        IReadOnlyList<string>? dependsOn = null,
        ResourceDeclarationPersistence persistence = ResourceDeclarationPersistence.Transient,
        bool overwritePersistedState = false,
        Action<ResourceDeclaration>? onChanged = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        ResourceDeclaration declaration;
        lock (_gate)
        {
            var normalized = Normalize(
                providerId,
                resourceId,
                parentResourceId,
                resourceGroupId,
                dependsOn ?? [],
                persistence,
                overwritePersistedState);

            declaration = _declarations.TryGetValue(normalized.ResourceId, out var existing)
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
        return new CloudShellResourceBuilder(builder, this, declaration.ResourceId);
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
        bool overwritePersistedState) =>
        new(
            providerId.Trim(),
            resourceId.Trim(),
            NormalizeResourceId(parentResourceId),
            NormalizeGroupId(resourceGroupId),
            DateTimeOffset.UtcNow,
            NormalizeDependencies(dependsOn),
            persistence,
            overwritePersistedState);

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
}

internal sealed class CloudShellResourceBuilder(
    ICloudShellBuilder cloudShellBuilder,
    ResourceDeclarationStore declarations,
    string resourceId) : ICloudShellResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder { get; } = cloudShellBuilder;

    public string ResourceId { get; } = resourceId;

    public ICloudShellResourceBuilder WithResourceGroup(string? resourceGroupId)
    {
        declarations.AssignToGroup(ResourceId, resourceGroupId);
        return this;
    }

    public ICloudShellResourceBuilder WithParent(string? parentResourceId)
    {
        declarations.AssignParent(ResourceId, parentResourceId);
        return this;
    }

    public ICloudShellResourceBuilder WithParent(ICloudShellResourceBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return WithParent(resource.ResourceId);
    }

    public ICloudShellResourceBuilder DependsOn(string resourceId)
    {
        var declaration = declarations.GetDeclaration(ResourceId)
            ?? throw new InvalidOperationException($"Resource '{ResourceId}' is not declared.");
        declarations.SetDependencies(
            ResourceId,
            declaration.DependsOn.Append(resourceId).ToArray());
        return this;
    }

    public ICloudShellResourceBuilder DependsOn(ICloudShellResourceBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return DependsOn(resource.ResourceId);
    }

    public ICloudShellResourceBuilder DependsOn(IEnumerable<string> resourceIds)
    {
        var declaration = declarations.GetDeclaration(ResourceId)
            ?? throw new InvalidOperationException($"Resource '{ResourceId}' is not declared.");
        declarations.SetDependencies(
            ResourceId,
            declaration.DependsOn.Concat(resourceIds).ToArray());
        return this;
    }

    public ICloudShellResourceBuilder DependsOn(IEnumerable<ICloudShellResourceBuilder> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return DependsOn(resources.Select(resource =>
        {
            ArgumentNullException.ThrowIfNull(resource);
            return resource.ResourceId;
        }));
    }

    public ICloudShellResourceBuilder WithReference(string resourceId) =>
        DependsOn(resourceId);

    public ICloudShellResourceBuilder WithReference(ICloudShellResourceBuilder resource) =>
        DependsOn(resource);

    public ICloudShellResourceBuilder WithReferences(IEnumerable<string> resourceIds) =>
        DependsOn(resourceIds);

    public ICloudShellResourceBuilder Persist(bool overwrite = false)
    {
        declarations.Persist(ResourceId, overwrite);
        return this;
    }
}
