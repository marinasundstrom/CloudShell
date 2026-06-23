namespace CloudShell.Providers.Applications;

internal sealed class ApplicationContainerHistoryService(
    ApplicationResourceStore store,
    ApplicationContainerDeploymentStore containerDeployments,
    ApplicationResourceDefinitionNormalizer definitionNormalizer) : IContainerApplicationHistoryOperations
{
    private static readonly ApplicationContainerRevisionService ContainerRevisionService = new();

    public IReadOnlyList<ApplicationContainerDeployment> GetContainerDeployments(string applicationId) =>
        containerDeployments.List(applicationId);

    public IReadOnlyList<ApplicationContainerRevisionHistoryEntry> GetContainerRevisions(string applicationId)
    {
        var revisions = containerDeployments.ListRevisions(applicationId);
        if (revisions.Count > 0)
        {
            return ContainerRevisionService.AssignHistoryNumbers(revisions);
        }

        var application = GetApplication(applicationId);
        return application is null
            ? []
            : ContainerRevisionService.AssignHistoryNumbers(
                ContainerRevisionService.CreateHistoryEntries(application));
    }

    private ApplicationResourceDefinition? GetApplication(string id) =>
        store.GetApplication(id) is { } application
            ? definitionNormalizer.Resolve(application)
            : null;
}
