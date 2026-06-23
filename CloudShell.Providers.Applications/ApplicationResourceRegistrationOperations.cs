using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationResourceRegistrationOperations(
    IApplicationResourceDefinitionSource definitions,
    ApplicationResourceDefinitionRegistrationService registrations) : IApplicationResourceRegistrationOperations
{
    public ApplicationResourceDefinition? GetApplication(string id) =>
        definitions.GetApplication(id);

    public IReadOnlyList<ApplicationResourceDefinition> GetApplications() =>
        definitions.GetApplications();

    public Task SetupApplicationAsync(
        ApplicationResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrationsStore,
        CancellationToken cancellationToken = default) =>
        registrations.SetupApplicationAsync(
            definition,
            resourceGroupId,
            registrationsStore,
            cancellationToken);
}
