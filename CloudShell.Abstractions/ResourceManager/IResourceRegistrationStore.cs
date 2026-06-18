namespace CloudShell.Abstractions.ResourceManager;

public interface IResourceRegistrationStore
{
    IReadOnlyList<ResourceRegistration> GetRegistrations();

    ResourceRegistration? GetRegistration(string resourceId);

    Task RegisterAsync(
        string providerId,
        string resourceId,
        string? resourceGroupId = null,
        IReadOnlyList<string>? dependsOn = null,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    Task AssignToGroupAsync(
        string resourceId,
        string? resourceGroupId,
        IReadOnlyList<string>? dependsOn = null,
        CancellationToken cancellationToken = default);

    Task SetDependenciesAsync(
        string resourceId,
        IReadOnlyList<string> dependsOn,
        CancellationToken cancellationToken = default);

    Task SetIdentityAsync(
        string resourceId,
        ResourceIdentityBinding? identity,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("This registration store does not support resource identity updates."));
}
