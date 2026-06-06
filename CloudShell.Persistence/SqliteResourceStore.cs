using CloudShell.Abstractions.ResourceManager;
using Microsoft.EntityFrameworkCore;

namespace CloudShell.Persistence;

public sealed class SqliteResourceStore(
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
                registration.RegisteredAt))
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
                registration.RegisteredAt);
    }

    public async Task RegisterAsync(
        string providerId,
        string resourceId,
        string? resourceGroupId = null,
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
                RegisteredAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            registration.ProviderId = providerId;
            registration.ResourceGroupId = NormalizeGroupId(resourceGroupId);
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
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var registration = await context.ResourceRegistrations
            .SingleOrDefaultAsync(item => item.ResourceId == resourceId, cancellationToken)
            ?? throw new InvalidOperationException($"Resource '{resourceId}' is not registered.");

        registration.ResourceGroupId = NormalizeGroupId(resourceGroupId);
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
}
