using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Persistence;

namespace CloudShell.ControlPlane.Authentication;

public sealed class AuthorizedResourceRegistrationStore(
    EfCoreResourceStore inner,
    ICloudShellAuthorizationService authorization) : IResourceRegistrationStore
{
    public IReadOnlyList<ResourceRegistration> GetRegistrations() =>
        inner.GetRegistrations()
            .Where(registration => CanAccess(
                registration,
                CloudShellPermissions.Resources.Read))
            .ToArray();

    public ResourceRegistration? GetRegistration(string resourceId)
    {
        var registration = inner.GetRegistration(resourceId);
        return registration is not null &&
               CanAccess(registration, CloudShellPermissions.Resources.Read)
            ? registration
            : null;
    }

    public async Task RegisterAsync(
        string providerId,
        string resourceId,
        string? resourceGroupId = null,
        CancellationToken cancellationToken = default)
    {
        var existing = inner.GetRegistration(resourceId);
        if (existing is not null)
        {
            EnsureAccess(existing, CloudShellPermissions.Resources.Manage);
        }

        EnsureAccess(resourceId, resourceGroupId, CloudShellPermissions.Resources.Create);
        await inner.RegisterAsync(providerId, resourceId, resourceGroupId, cancellationToken);
    }

    public async Task RemoveAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var registration = inner.GetRegistration(resourceId);
        if (registration is null)
        {
            return;
        }

        EnsureAccess(registration, CloudShellPermissions.Resources.Manage);
        await inner.RemoveAsync(resourceId, cancellationToken);
    }

    public async Task AssignToGroupAsync(
        string resourceId,
        string? resourceGroupId,
        CancellationToken cancellationToken = default)
    {
        var registration = inner.GetRegistration(resourceId)
            ?? throw new InvalidOperationException($"Resource '{resourceId}' is not registered.");

        EnsureAccess(registration, CloudShellPermissions.Resources.Manage);
        EnsureAccess(resourceId, resourceGroupId, CloudShellPermissions.Resources.Manage);
        await inner.AssignToGroupAsync(resourceId, resourceGroupId, cancellationToken);
    }

    private bool CanAccess(ResourceRegistration registration, string permission) =>
        authorization.CanAccessResource(
            registration.ResourceId,
            registration.ResourceGroupId,
            permission);

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
}
