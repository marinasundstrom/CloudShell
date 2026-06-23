namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    private static readonly ApplicationContainerRevisionService ContainerRevisionService = new();
    private static readonly ContainerApplicationDeploymentPlanner ContainerDeploymentPlanner = new();

    private static IReadOnlyList<ApplicationContainerRevisionHistoryEntry> CreateContainerRevisionHistoryEntries(
        ApplicationResourceDefinition application) =>
        ContainerRevisionService.CreateHistoryEntries(application);

    private static IReadOnlyList<ApplicationContainerRevisionHistoryEntry> AssignContainerRevisionHistoryNumbers(
        IReadOnlyList<ApplicationContainerRevisionHistoryEntry> revisions) =>
        ContainerRevisionService.AssignHistoryNumbers(revisions);

    private static string GetEffectiveContainerRevision(ApplicationResourceDefinition application) =>
        ContainerRevisionService.GetEffectiveRevision(application);
}
