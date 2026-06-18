using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Persistence;

namespace CloudShell.ControlPlane.Authentication;

public sealed class AuthorizedResourceRegistrationStore(
    EfCoreResourceStore inner,
    ResourceDeclarationStore declarations,
    ICloudShellAuthorizationService authorization) : IResourceRegistrationStore
{
    public IReadOnlyList<ResourceRegistration> GetRegistrations() =>
        inner.GetRegistrations()
            .Concat(declarations.GetDeclarations()
                .Where(declaration => inner.GetRegistration(declaration.ResourceId) is null)
                .Select(ToRegistration))
            .Where(registration => CanAccess(
                registration,
                CloudShellPermissions.Resources.Read))
            .ToArray();

    public ResourceRegistration? GetRegistration(string resourceId)
    {
        var registration = inner.GetRegistration(resourceId)
            ?? (declarations.GetDeclaration(resourceId) is { } declaration
                ? ToRegistration(declaration)
                : null);
        return registration is not null &&
               CanAccess(registration, CloudShellPermissions.Resources.Read)
            ? registration
            : null;
    }

    public async Task RegisterAsync(
        string providerId,
        string resourceId,
        string? resourceGroupId = null,
        IReadOnlyList<string>? dependsOn = null,
        CancellationToken cancellationToken = default)
    {
        var existing = inner.GetRegistration(resourceId);
        if (existing is not null)
        {
            EnsureAccess(existing, CloudShellPermissions.Resources.Manage);
        }

        EnsureAccess(resourceId, resourceGroupId, CloudShellPermissions.Resources.Create);
        await inner.RegisterAsync(providerId, resourceId, resourceGroupId, dependsOn, cancellationToken);
    }

    public async Task RemoveAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var registration = inner.GetRegistration(resourceId);
        if (registration is null)
        {
            if (declarations.GetDeclaration(resourceId) is not null)
            {
                declarations.Remove(resourceId);
            }

            return;
        }

        EnsureAccess(registration, CloudShellPermissions.Resources.Manage);
        await inner.RemoveAsync(resourceId, cancellationToken);
    }

    public async Task AssignToGroupAsync(
        string resourceId,
        string? resourceGroupId,
        IReadOnlyList<string>? dependsOn = null,
        CancellationToken cancellationToken = default)
    {
        var registration = inner.GetRegistration(resourceId);
        if (registration is null)
        {
            var declaration = declarations.GetDeclaration(resourceId)
                ?? throw new InvalidOperationException($"Resource '{resourceId}' is not registered.");
            registration = ToRegistration(declaration);

            EnsureAccess(registration, CloudShellPermissions.Resources.Manage);
            EnsureAccess(resourceId, resourceGroupId, CloudShellPermissions.Resources.Manage);
            declarations.AssignToGroup(resourceId, resourceGroupId);
            if (dependsOn is not null)
            {
                declarations.SetDependencies(resourceId, dependsOn);
            }

            return;
        }

        EnsureAccess(registration, CloudShellPermissions.Resources.Manage);
        EnsureAccess(resourceId, resourceGroupId, CloudShellPermissions.Resources.Manage);
        await inner.AssignToGroupAsync(resourceId, resourceGroupId, dependsOn, cancellationToken);
    }

    public async Task SetDependenciesAsync(
        string resourceId,
        IReadOnlyList<string> dependsOn,
        CancellationToken cancellationToken = default)
    {
        var registration = inner.GetRegistration(resourceId);
        if (registration is null)
        {
            var declaration = declarations.GetDeclaration(resourceId)
                ?? throw new InvalidOperationException($"Resource '{resourceId}' is not registered.");
            registration = ToRegistration(declaration);

            EnsureAccess(registration, CloudShellPermissions.Resources.Manage);
            declarations.SetDependencies(resourceId, dependsOn);
            return;
        }

        EnsureAccess(registration, CloudShellPermissions.Resources.Manage);
        await inner.SetDependenciesAsync(resourceId, dependsOn, cancellationToken);
    }

    public async Task SetIdentityAsync(
        string resourceId,
        ResourceIdentityBinding? identity,
        CancellationToken cancellationToken = default)
    {
        var registration = inner.GetRegistration(resourceId);
        if (registration is null)
        {
            var declaration = declarations.GetDeclaration(resourceId)
                ?? throw new InvalidOperationException($"Resource '{resourceId}' is not registered.");
            registration = ToRegistration(declaration);

            EnsureAccess(registration, CloudShellPermissions.Resources.Manage);
            declarations.SetIdentity(resourceId, identity);
            return;
        }

        EnsureAccess(registration, CloudShellPermissions.Resources.Manage);
        await inner.SetIdentityAsync(resourceId, identity, cancellationToken);
    }

    private bool CanAccess(ResourceRegistration registration, string permission) =>
        authorization.CanAccessResource(
            registration.ResourceId,
            registration.ResourceGroupId,
            permission) ||
        string.Equals(permission, CloudShellPermissions.Resources.Read, StringComparison.OrdinalIgnoreCase) &&
        authorization.CanAccessResource(
            registration.ResourceId,
            registration.ResourceGroupId,
            CloudShellPermissions.Resources.Manage);

    private void EnsureAccess(ResourceRegistration registration, string permission) =>
        EnsureAccess(registration.ResourceId, registration.ResourceGroupId, permission);

    private void EnsureAccess(string resourceId, string? resourceGroupId, string permission)
    {
        if (!authorization.CanAccessResource(resourceId, resourceGroupId, permission))
        {
            throw new UnauthorizedAccessException(
                $"The '{permission}' permission is required for resource '{resourceId}'.");
        }
    }

    private static ResourceRegistration ToRegistration(ResourceDeclaration declaration) =>
        new(
            declaration.ResourceId,
            declaration.ProviderId,
            declaration.ResourceGroupId,
            declaration.DeclaredAt,
            declaration.DependsOn,
            declaration.IdentityBinding);
}
