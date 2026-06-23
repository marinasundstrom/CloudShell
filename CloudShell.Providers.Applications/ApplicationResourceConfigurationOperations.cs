using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationResourceConfigurationOperations(
    IApplicationResourceDefinitionSource definitions,
    IApplicationResourceRunningStateOperations runningState,
    ApplicationResourceDefinitionRegistrationService registrations) : IApplicationResourceConfigurationOperations
{
    public ApplicationResourceDefinition? GetApplication(string id) =>
        definitions.GetApplication(id);

    public IReadOnlyList<ApplicationResourceDefinition> GetApplications() =>
        definitions.GetApplications();

    public bool IsRunning(string applicationId) =>
        runningState.IsRunning(applicationId);

    public Task UpdateApplicationAsync(
        ApplicationResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrationsStore,
        CancellationToken cancellationToken = default) =>
        registrations.UpdateApplicationAsync(
            definition,
            resourceGroupId,
            registrationsStore,
            cancellationToken);
}
