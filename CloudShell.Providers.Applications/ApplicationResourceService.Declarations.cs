using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    public ApplicationResourceDefinition? GetApplication(string id) =>
        _applicationCatalog.GetApplication(id);

    public IReadOnlyList<ApplicationResourceDefinition> GetApplications() =>
        _applicationCatalog.GetApplications();

    public IReadOnlyList<ApplicationContainerDeployment> GetContainerDeployments(string applicationId) =>
        _applicationCatalog.GetContainerDeployments(applicationId);

    public IReadOnlyList<ApplicationContainerRevisionHistoryEntry> GetContainerRevisions(string applicationId) =>
        _applicationCatalog.GetContainerRevisions(applicationId);

    public bool CanApplyDeclaration(ResourceDeclaration declaration) =>
        ApplicationResourceProviderIds.IsApplicationProvider(declaration.ProviderId);

    public bool CanEvaluateAutoStartPolicy(ResourceDeclaration declaration) =>
        CanApplyDeclaration(declaration);

    public ResourceAutoStartPolicy GetAutoStartPolicy(ResourceDeclaration declaration) =>
        new(
            StartOnControlPlaneStart: true,
            StartAsDependency: true,
            StartAfterCreate: false);

    public Task ApplyDeclarationAsync(
        ResourceDeclaration declaration,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var declaredApplication = options.DeclaredApplications.FirstOrDefault(application =>
            string.Equals(application.Definition.Id, declaration.ResourceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Application resource declaration '{declaration.ResourceId}' was not found.");

        if (!declaration.OverwritePersistedState &&
            (registrations.GetRegistration(declaration.ResourceId) is not null ||
             store.GetApplication(declaration.ResourceId) is not null))
        {
            return Task.CompletedTask;
        }

        var dependencies = declaredApplication.Definition.DependsOn
            .Concat(declaration.DependsOn)
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
            .Select(dependency => dependency.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return SetupApplicationAsync(
            declaredApplication.Definition with { DependsOn = dependencies },
            declaration.ResourceGroupId,
            registrations,
            cancellationToken);
    }
}
