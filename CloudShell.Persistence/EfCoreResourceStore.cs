using CloudShell.Abstractions.ResourceManager;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CloudShell.Persistence;

public sealed class EfCoreResourceStore(
    IDbContextFactory<CloudShellDbContext> contextFactory) :
    IResourceRegistrationStore,
    IResourceGroupStore
{
    public IReadOnlyList<ResourceRegistration> GetRegistrations()
    {
        using var context = contextFactory.CreateDbContext();

        return context.ResourceRegistrations
            .AsNoTracking()
            .OrderBy(registration => registration.RegisteredAt)
            .Select(registration => new ResourceRegistration(
                registration.ResourceId,
                registration.ProviderId,
                registration.ResourceGroupId,
                registration.RegisteredAt,
                DeserializeDependencies(registration.DependsOnJson)))
            .ToArray();
    }

    public ResourceRegistration? GetRegistration(string resourceId)
    {
        using var context = contextFactory.CreateDbContext();
        var registration = context.ResourceRegistrations
            .AsNoTracking()
            .SingleOrDefault(item => item.ResourceId == resourceId);

        return registration is null
            ? null
            : new ResourceRegistration(
                registration.ResourceId,
                registration.ProviderId,
                registration.ResourceGroupId,
                registration.RegisteredAt,
                DeserializeDependencies(registration.DependsOnJson));
    }

    public async Task RegisterAsync(
        string providerId,
        string resourceId,
        string? resourceGroupId = null,
        IReadOnlyList<string>? dependsOn = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var registration = await context.ResourceRegistrations
            .SingleOrDefaultAsync(item => item.ResourceId == resourceId, cancellationToken);

        if (registration is null)
        {
            context.ResourceRegistrations.Add(new ResourceRegistrationEntity
            {
                ResourceId = resourceId,
                ProviderId = providerId,
                ResourceGroupId = NormalizeGroupId(resourceGroupId),
                RegisteredAt = DateTimeOffset.UtcNow,
                DependsOnJson = SerializeDependencies(dependsOn ?? [])
            });
        }
        else
        {
            registration.ProviderId = providerId;
            registration.ResourceGroupId = NormalizeGroupId(resourceGroupId);
            if (dependsOn is not null)
            {
                registration.DependsOnJson = SerializeDependencies(dependsOn);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var registration = await context.ResourceRegistrations
            .SingleOrDefaultAsync(item => item.ResourceId == resourceId, cancellationToken);

        if (registration is null)
        {
            return;
        }

        context.ResourceRegistrations.Remove(registration);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AssignToGroupAsync(
        string resourceId,
        string? resourceGroupId,
        IReadOnlyList<string>? dependsOn = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var registration = await context.ResourceRegistrations
            .SingleOrDefaultAsync(item => item.ResourceId == resourceId, cancellationToken)
            ?? throw new InvalidOperationException($"Resource '{resourceId}' is not registered.");

        registration.ResourceGroupId = NormalizeGroupId(resourceGroupId);
        if (dependsOn is not null)
        {
            registration.DependsOnJson = SerializeDependencies(dependsOn);
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetDependenciesAsync(
        string resourceId,
        IReadOnlyList<string> dependsOn,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var registration = await context.ResourceRegistrations
            .SingleOrDefaultAsync(item => item.ResourceId == resourceId, cancellationToken)
            ?? throw new InvalidOperationException($"Resource '{resourceId}' is not registered.");

        registration.DependsOnJson = SerializeDependencies(dependsOn);
        await context.SaveChangesAsync(cancellationToken);
    }

    public IReadOnlyList<ResourceGroup> GetResourceGroups()
    {
        using var context = contextFactory.CreateDbContext();
        var registrations = context.ResourceRegistrations
            .AsNoTracking()
            .Where(registration => registration.ResourceGroupId != null)
            .ToArray()
            .GroupBy(registration => registration.ResourceGroupId!)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(item => item.ResourceId).ToArray());

        return context.ResourceGroups
            .AsNoTracking()
            .OrderBy(group => group.Name)
            .ToArray()
            .Select(group => new ResourceGroup(
                group.Id,
                group.Name,
                group.Description,
                registrations.GetValueOrDefault(group.Id) ?? []))
            .ToArray();
    }

    public ResourceGroup? GetGroupForResource(string resourceId)
    {
        using var context = contextFactory.CreateDbContext();
        var registration = context.ResourceRegistrations
            .AsNoTracking()
            .SingleOrDefault(item => item.ResourceId == resourceId);

        if (registration?.ResourceGroupId is null)
        {
            return null;
        }

        var group = context.ResourceGroups
            .AsNoTracking()
            .SingleOrDefault(item => item.Id == registration.ResourceGroupId);

        if (group is null)
        {
            return null;
        }

        var resourceIds = context.ResourceRegistrations
            .AsNoTracking()
            .Where(item => item.ResourceGroupId == group.Id)
            .Select(item => item.ResourceId)
            .ToArray();

        return new ResourceGroup(group.Id, group.Name, group.Description, resourceIds);
    }

    public async Task<ResourceGroup> CreateAsync(
        string name,
        string description,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var group = new ResourceGroupEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name.Trim(),
            Description = description.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        context.ResourceGroups.Add(group);
        await context.SaveChangesAsync(cancellationToken);

        return new ResourceGroup(group.Id, group.Name, group.Description, []);
    }

    private static string? NormalizeGroupId(string? resourceGroupId) =>
        string.IsNullOrWhiteSpace(resourceGroupId) ? null : resourceGroupId;

    private static string SerializeDependencies(IReadOnlyList<string> dependsOn) =>
        JsonSerializer.Serialize(NormalizeDependencies(dependsOn));

    private static IReadOnlyList<string> DeserializeDependencies(string? dependsOnJson)
    {
        if (string.IsNullOrWhiteSpace(dependsOnJson))
        {
            return [];
        }

        return NormalizeDependencies(
            JsonSerializer.Deserialize<IReadOnlyList<string>>(dependsOnJson) ?? []);
    }

    private static IReadOnlyList<string> NormalizeDependencies(IReadOnlyList<string> dependsOn) =>
        dependsOn
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
            .Select(dependency => dependency.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
