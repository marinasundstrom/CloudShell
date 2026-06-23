namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    private static readonly ApplicationContainerRevisionService ContainerRevisionService = new();

    private static IReadOnlyList<ApplicationContainerRevision> AppendContainerRevision(
        ApplicationResourceDefinition application,
        string revisionId,
        string image,
        int requestedReplicas,
        string changeKind,
        string? triggeredBy) =>
        ContainerRevisionService.AppendRevision(
            application,
            revisionId,
            image,
            requestedReplicas,
            changeKind,
            triggeredBy);

    private static ApplicationContainerRevisionHistoryEntry? CreateBasedOnContainerRevisionHistoryEntry(
        ApplicationResourceDefinition application,
        string? basedOnRevisionId) =>
        ContainerRevisionService.CreateBasedOnHistoryEntry(application, basedOnRevisionId);

    private static IReadOnlyList<ApplicationContainerRevisionHistoryEntry> CreateContainerRevisionHistoryEntries(
        ApplicationResourceDefinition application) =>
        ContainerRevisionService.CreateHistoryEntries(application);

    private static IReadOnlyList<ApplicationContainerRevision> AssignContainerRevisionNumbers(
        IReadOnlyList<ApplicationContainerRevision> revisions) =>
        ContainerRevisionService.AssignRevisionNumbers(revisions);

    private static IReadOnlyList<ApplicationContainerRevisionHistoryEntry> AssignContainerRevisionHistoryNumbers(
        IReadOnlyList<ApplicationContainerRevisionHistoryEntry> revisions) =>
        ContainerRevisionService.AssignHistoryNumbers(revisions);

    private static string GetEffectiveContainerRevision(ApplicationResourceDefinition application) =>
        ContainerRevisionService.GetEffectiveRevision(application);
}
