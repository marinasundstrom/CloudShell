using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationResourceDeclarationOperations(
    ApplicationProviderOptions options,
    IApplicationResourceDefinitionSource definitions,
    IApplicationResourceRegistrationOperations registrations) : IApplicationResourceDeclarationOperations
{
    public ResourceAutoStartPolicy GetAutoStartPolicy(ResourceDeclaration declaration) =>
        new(
            StartOnControlPlaneStart: true,
            StartAsDependency: true,
            StartAfterCreate: false);

    public Task ApplyDeclarationAsync(
        ResourceDeclaration declaration,
        IResourceRegistrationStore registrationStore,
        CancellationToken cancellationToken = default)
    {
        var declaredApplication = options.DeclaredApplications.FirstOrDefault(application =>
            string.Equals(application.Definition.Id, declaration.ResourceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Application resource declaration '{declaration.ResourceId}' was not found.");

        if (!declaration.OverwritePersistedState &&
            (registrationStore.GetRegistration(declaration.ResourceId) is not null ||
             definitions.GetApplication(declaration.ResourceId) is not null))
        {
            return Task.CompletedTask;
        }

        var dependencies = declaredApplication.Definition.DependsOn
            .Concat(declaration.DependsOn)
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
            .Select(dependency => dependency.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return registrations.SetupApplicationAsync(
            declaredApplication.Definition with { DependsOn = dependencies },
            declaration.ResourceGroupId,
            registrationStore,
            cancellationToken);
    }
}
