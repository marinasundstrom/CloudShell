namespace CloudShell.Providers.Applications;

public sealed class ApplicationResourceCatalog(
    ApplicationResourceStore store,
    ApplicationContainerDeploymentStore containerDeployments,
    ApplicationResourceDefinitionNormalizer definitionNormalizer)
{
    private readonly ApplicationContainerRevisionService _containerRevisionService = new();

    public ApplicationResourceDefinition? GetApplication(string id) =>
        store.GetApplication(id) is { } application
            ? Resolve(application)
            : null;

    public IReadOnlyList<ApplicationResourceDefinition> GetApplications() => store
        .GetApplications()
        .Select(Resolve)
        .ToArray();

    public IReadOnlyList<ApplicationContainerDeployment> GetContainerDeployments(string applicationId) =>
        containerDeployments.List(applicationId);

    public IReadOnlyList<ApplicationContainerRevisionHistoryEntry> GetContainerRevisions(string applicationId)
    {
        var revisions = containerDeployments.ListRevisions(applicationId);
        if (revisions.Count > 0)
        {
            return _containerRevisionService.AssignHistoryNumbers(revisions);
        }

        var application = GetApplication(applicationId);
        return application is null
            ? []
            : _containerRevisionService.AssignHistoryNumbers(
                _containerRevisionService.CreateHistoryEntries(application));
    }

    public ApplicationResourceDefinition Resolve(ApplicationResourceDefinition definition) =>
        definitionNormalizer.Resolve(definition);
}
