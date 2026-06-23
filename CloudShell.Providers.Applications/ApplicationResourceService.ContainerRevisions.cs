namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    private static IReadOnlyList<ApplicationContainerRevision> AppendContainerRevision(
        ApplicationResourceDefinition application,
        string revisionId,
        string image,
        int requestedReplicas,
        string changeKind,
        string? triggeredBy)
    {
        var revisions = application.ContainerRevisions.ToList();
        var basedOnRevisionId = NormalizeNullable(application.ContainerRevision);
        if (!string.IsNullOrWhiteSpace(basedOnRevisionId) &&
            revisions.All(revision => !string.Equals(revision.Id, basedOnRevisionId, StringComparison.OrdinalIgnoreCase)))
        {
            revisions.Add(new ApplicationContainerRevision(
                basedOnRevisionId,
                NormalizeNullable(application.ContainerImage) ?? "unresolved",
                Math.Max(1, application.Replicas),
                DateTimeOffset.UtcNow,
                ApplicationContainerRevisionChangeKinds.Initial));
        }

        revisions.RemoveAll(revision =>
            string.Equals(revision.Id, revisionId, StringComparison.OrdinalIgnoreCase));
        revisions.Add(new ApplicationContainerRevision(
            revisionId,
            image,
            Math.Max(1, requestedReplicas),
            DateTimeOffset.UtcNow,
            changeKind,
            basedOnRevisionId,
            NormalizeNullable(triggeredBy)));
        return AssignContainerRevisionNumbers(revisions);
    }

    private static ApplicationContainerRevisionHistoryEntry? CreateBasedOnContainerRevisionHistoryEntry(
        ApplicationResourceDefinition application,
        string? basedOnRevisionId)
    {
        if (string.IsNullOrWhiteSpace(basedOnRevisionId))
        {
            return null;
        }

        var basedOnRevision = application.ContainerRevisions.FirstOrDefault(revision =>
            string.Equals(revision.Id, basedOnRevisionId, StringComparison.OrdinalIgnoreCase));
        return new ApplicationContainerRevisionHistoryEntry(
            basedOnRevisionId,
            application.Id,
            NormalizeNullable(basedOnRevision?.Image) ?? NormalizeNullable(application.ContainerImage) ?? "unresolved",
            Math.Max(1, basedOnRevision?.RequestedReplicas ?? application.Replicas),
            basedOnRevision?.CreatedAt ?? DateTimeOffset.UtcNow,
            ApplicationContainerRevisionStatuses.Superseded,
            NormalizeNullable(basedOnRevision?.ChangeKind) ?? ApplicationContainerRevisionChangeKinds.Initial,
            NormalizeNullable(basedOnRevision?.BasedOnRevisionId),
            NormalizeNullable(basedOnRevision?.ProvisionedBy),
            RevisionNumber: Math.Max(0, basedOnRevision?.RevisionNumber ?? 0));
    }

    private static IReadOnlyList<ApplicationContainerRevisionHistoryEntry> CreateContainerRevisionHistoryEntries(
        ApplicationResourceDefinition application) =>
        application.ContainerRevisions
            .Where(revision => !string.IsNullOrWhiteSpace(revision.Id))
            .Select(revision => new ApplicationContainerRevisionHistoryEntry(
                revision.Id,
                application.Id,
                NormalizeNullable(revision.Image) ?? NormalizeNullable(application.ContainerImage) ?? "unresolved",
                Math.Max(1, revision.RequestedReplicas),
                revision.CreatedAt,
                string.Equals(revision.Id, application.ContainerRevision, StringComparison.OrdinalIgnoreCase)
                    ? ApplicationContainerRevisionStatuses.Active
                    : ApplicationContainerRevisionStatuses.Superseded,
                NormalizeNullable(revision.ChangeKind) ?? ApplicationContainerRevisionChangeKinds.ImageDeployment,
                NormalizeNullable(revision.BasedOnRevisionId),
                NormalizeNullable(revision.ProvisionedBy),
                RevisionNumber: Math.Max(0, revision.RevisionNumber)))
            .ToArray();

    private static IReadOnlyList<ApplicationContainerRevision> AssignContainerRevisionNumbers(
        IReadOnlyList<ApplicationContainerRevision> revisions)
    {
        var numbers = revisions
            .OrderBy(revision => revision.CreatedAt)
            .ThenBy(revision => revision.Id, StringComparer.OrdinalIgnoreCase)
            .Select((revision, index) => new { revision.Id, Number = index + 1 })
            .ToDictionary(item => item.Id, item => item.Number, StringComparer.OrdinalIgnoreCase);
        return revisions
            .Select(revision => revision with { RevisionNumber = numbers[revision.Id] })
            .ToArray();
    }

    private static IReadOnlyList<ApplicationContainerRevisionHistoryEntry> AssignContainerRevisionHistoryNumbers(
        IReadOnlyList<ApplicationContainerRevisionHistoryEntry> revisions)
    {
        var numbers = revisions
            .OrderBy(revision => revision.CreatedAt)
            .ThenBy(revision => revision.Id, StringComparer.OrdinalIgnoreCase)
            .Select((revision, index) => new { revision.Id, Number = index + 1 })
            .ToDictionary(item => item.Id, item => item.Number, StringComparer.OrdinalIgnoreCase);
        return revisions
            .Select(revision => revision with { RevisionNumber = numbers[revision.Id] })
            .ToArray();
    }

    private static string GetEffectiveContainerRevision(ApplicationResourceDefinition application) =>
        NormalizeNullable(application.ContainerRevision) ?? "unrevisioned";
}
